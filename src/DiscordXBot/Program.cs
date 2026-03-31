using DiscordXBot;
using DiscordXBot.Configuration;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
	.AddOptions<DiscordOptions>()
	.Bind(builder.Configuration.GetSection(DiscordOptions.SectionName));

builder.Services
	.AddOptions<RssBridgeOptions>()
	.Bind(builder.Configuration.GetSection(RssBridgeOptions.SectionName));

builder.Services
	.AddOptions<PollingOptions>()
	.Bind(builder.Configuration.GetSection(PollingOptions.SectionName));

builder.Services
	.AddOptions<RetryOptions>()
	.Bind(builder.Configuration.GetSection(RetryOptions.SectionName));

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
