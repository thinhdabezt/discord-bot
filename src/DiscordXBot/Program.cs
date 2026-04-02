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
	.Bind(builder.Configuration.GetSection(RssBridgeOptions.SectionName))
	.Validate(options => !string.IsNullOrWhiteSpace(options.BaseUrl), "RssBridge BaseUrl is required. Set RSSBRIDGE__BASEURL.")
	.ValidateOnStart();

builder.Services
	.AddOptions<FeedProviderOptions>()
	.Bind(builder.Configuration.GetSection(FeedProviderOptions.SectionName));

builder.Services
	.AddOptions<ApifyFallbackOptions>()
	.Bind(builder.Configuration.GetSection(ApifyFallbackOptions.SectionName));

builder.Services
	.AddOptions<RssBridgeFallbackOptions>()
	.Bind(builder.Configuration.GetSection(RssBridgeFallbackOptions.SectionName));

builder.Services
	.AddOptions<PollingOptions>()
	.Bind(builder.Configuration.GetSection(PollingOptions.SectionName));

builder.Services
	.AddOptions<RetryOptions>()
	.Bind(builder.Configuration.GetSection(RetryOptions.SectionName));

builder.Services
	.AddOptions<PublishOptions>()
	.Bind(builder.Configuration.GetSection(PublishOptions.SectionName));

var connectionString = Environment.GetEnvironmentVariable("CONNECTIONSTRINGS__DEFAULT");
if (string.IsNullOrWhiteSpace(connectionString))
{
	throw new OptionsValidationException(
		"ConnectionStrings:Default",
		typeof(string),
		["Connection string is required. Set CONNECTIONSTRINGS__DEFAULT environment variable."]);
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
builder.Services.AddSingleton<ApifyFacebookClient>();
builder.Services.AddSingleton<FeedUrlResolver>();
builder.Services.AddSingleton<TweetContentParser>();
builder.Services.AddSingleton<DiscordPublisher>();

builder.Services.AddHostedService<DiscordGatewayHostedService>();
builder.Services.AddHostedService<InteractionHandlerHostedService>();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
