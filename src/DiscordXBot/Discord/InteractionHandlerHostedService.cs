using System.Reflection;
using DiscordXBot.Configuration;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Options;

namespace DiscordXBot.Discord;

public sealed class InteractionHandlerHostedService(
    DiscordSocketClient client,
    InteractionService interactionService,
    IServiceProvider serviceProvider,
    IOptions<DiscordOptions> options,
    ILogger<InteractionHandlerHostedService> logger) : IHostedService
{
    private readonly DiscordSocketClient _client = client;
    private readonly InteractionService _interactionService = interactionService;
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly IOptions<DiscordOptions> _options = options;
    private readonly ILogger<InteractionHandlerHostedService> _logger = logger;
    private readonly SemaphoreSlim _registrationLock = new(1, 1);
    private bool _isRegistered;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _interactionService.AddModulesAsync(Assembly.GetExecutingAssembly(), _serviceProvider);

        _client.Ready += OnReadyAsync;
        _client.InteractionCreated += OnInteractionCreatedAsync;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _client.Ready -= OnReadyAsync;
        _client.InteractionCreated -= OnInteractionCreatedAsync;
        return Task.CompletedTask;
    }

    private async Task OnReadyAsync()
    {
        await _registrationLock.WaitAsync();
        try
        {
            if (_isRegistered)
            {
                return;
            }

            var devGuildId = _options.Value.DevGuildId;
            if (devGuildId.HasValue)
            {
                await _interactionService.RegisterCommandsToGuildAsync(devGuildId.Value, true);
                _logger.LogInformation("Registered slash commands to guild {GuildId}", devGuildId.Value);
            }
            else
            {
                await _interactionService.RegisterCommandsGloballyAsync(true);
                _logger.LogInformation("Registered slash commands globally.");
            }

            _isRegistered = true;
        }
        finally
        {
            _registrationLock.Release();
        }
    }

    private async Task OnInteractionCreatedAsync(SocketInteraction interaction)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var context = new SocketInteractionContext(_client, interaction);

            if (interaction is SocketSlashCommand slash)
            {
                _logger.LogInformation(
                    "Received slash command {CommandName} from user {UserId} in guild {GuildId}",
                    slash.Data.Name,
                    slash.User.Id,
                    slash.GuildId);

                if (!interaction.HasResponded)
                {
                    await interaction.DeferAsync(ephemeral: true);
                    _logger.LogInformation("Deferred slash command {CommandName} immediately.", slash.Data.Name);
                }
            }

            _logger.LogInformation("Executing slash command handler.");
            var executeTask = _interactionService.ExecuteCommandAsync(context, scope.ServiceProvider);
            var completedTask = await Task.WhenAny(executeTask, Task.Delay(TimeSpan.FromSeconds(30)));

            if (!ReferenceEquals(completedTask, executeTask))
            {
                _logger.LogError("Slash command execution timed out after 30 seconds.");

                if (interaction.HasResponded)
                {
                    await interaction.FollowupAsync(
                        "Command is taking too long to execute. Please try again in a moment.",
                        ephemeral: true);
                    return;
                }

                await interaction.RespondAsync(
                    "Command is taking too long to execute. Please try again in a moment.",
                    ephemeral: true);
                return;
            }

            var result = await executeTask;
            _logger.LogInformation("Slash command handler finished. Success={Success}", result.IsSuccess);

            if (result.IsSuccess)
            {
                return;
            }

            _logger.LogWarning(
                "Slash command execution failed. Error: {Error}, Reason: {Reason}",
                result.Error,
                result.ErrorReason);

            if (interaction.HasResponded)
            {
                await interaction.FollowupAsync($"Command failed: {result.ErrorReason}", ephemeral: true);
                return;
            }

            await interaction.RespondAsync($"Command failed: {result.ErrorReason}", ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled interaction execution error.");

            if (interaction.HasResponded)
            {
                await interaction.FollowupAsync("An unexpected error occurred while processing the command.", ephemeral: true);
                return;
            }

            if (!interaction.HasResponded)
            {
                await interaction.RespondAsync("An unexpected error occurred while processing the command.", ephemeral: true);
            }
        }
    }
}
