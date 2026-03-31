using DiscordXBot;
using DiscordXBot.Configuration;
using DiscordXBot.Data;
using Microsoft.EntityFrameworkCore;

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

var connectionString = builder.Configuration.GetConnectionString("Default")
	?? "Host=localhost;Port=5432;Database=discordbot;Username=postgres;Password=postgres";

builder.Services.AddDbContext<BotDbContext>(options => options.UseNpgsql(connectionString));

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
