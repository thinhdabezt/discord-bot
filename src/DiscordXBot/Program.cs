using DiscordXBot;
using DiscordXBot.Configuration;
using DiscordXBot.Data;
using DiscordXBot.Discord;
using DiscordXBot.Services;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
	.AddOptions<DiscordOptions>()
	.Bind(builder.Configuration.GetSection(DiscordOptions.SectionName))
	.Validate(options => !string.IsNullOrWhiteSpace(options.Token), "Discord token is required")
	.ValidateOnStart();

builder.Services
	.AddOptions<RssBridgeOptions>()
	.Bind(builder.Configuration.GetSection(RssBridgeOptions.SectionName));

builder.Services
	.AddOptions<PollingOptions>()
	.Bind(builder.Configuration.GetSection(PollingOptions.SectionName));

builder.Services
	.AddOptions<RetryOptions>()
	.Bind(builder.Configuration.GetSection(RetryOptions.SectionName));

builder.Services
	.AddOptions<PublishOptions>()
	.Bind(builder.Configuration.GetSection(PublishOptions.SectionName));

var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
{
	throw new OptionsValidationException(
		"ConnectionStrings:Default",
		typeof(string),
		["Connection string is required. Set ConnectionStrings:Default via appsettings or environment variables."]);
}

builder.Services.AddDbContext<BotDbContext>(options => options.UseNpgsql(connectionString));

builder.Services.AddHttpClient();

builder.Services.AddSingleton(_ => new DiscordSocketClient(new DiscordSocketConfig
{
	GatewayIntents = GatewayIntents.Guilds,
	AlwaysDownloadUsers = false
}));

builder.Services.AddSingleton(provider =>
	new InteractionService(
		provider.GetRequiredService<DiscordSocketClient>(),
		new InteractionServiceConfig
		{
			DefaultRunMode = RunMode.Sync,
			UseCompiledLambda = true
		}));
builder.Services.AddSingleton<RssBridgeClient>();
builder.Services.AddSingleton<TweetContentParser>();
builder.Services.AddSingleton<DiscordPublisher>();

builder.Services.AddHostedService<DiscordGatewayHostedService>();
builder.Services.AddHostedService<InteractionHandlerHostedService>();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
