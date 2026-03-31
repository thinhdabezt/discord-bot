using DiscordXBot.Configuration;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Options;

namespace DiscordXBot.Discord;

public sealed class DiscordGatewayHostedService(
    DiscordSocketClient client,
    IOptions<DiscordOptions> options,
    ILogger<DiscordGatewayHostedService> logger) : IHostedService
{
    private readonly DiscordSocketClient _client = client;
    private readonly IOptions<DiscordOptions> _options = options;
    private readonly ILogger<DiscordGatewayHostedService> _logger = logger;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _client.Log += OnDiscordLogAsync;

        var token = _options.Value.Token;
        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogWarning("Discord token is empty. Gateway start is skipped.");
            return;
        }

        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();

        _logger.LogInformation("Discord gateway startup requested.");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _client.Log -= OnDiscordLogAsync;

        if (_client.LoginState != LoginState.LoggedIn)
        {
            return;
        }

        await _client.StopAsync();
        await _client.LogoutAsync();
    }

    private Task OnDiscordLogAsync(LogMessage message)
    {
        var level = message.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Debug,
            LogSeverity.Debug => LogLevel.Trace,
            _ => LogLevel.Information
        };

        _logger.Log(level, message.Exception, "Discord: {Source} - {Message}", message.Source, message.Message);
        return Task.CompletedTask;
    }
}
