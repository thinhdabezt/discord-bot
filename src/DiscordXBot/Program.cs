using DiscordXBot;
using DiscordXBot.Configuration;
using DiscordXBot.Data;
using DiscordXBot.Discord;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
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
	?? "Host=db.rmaitvguorarjzaifgkc.supabase.co;Port=5432;Database=postgres;Username=postgres;Password=Jm8jZRnFbe5Pi6sq;SSL Mode=Require;Trust Server Certificate=true";

builder.Services.AddDbContext<BotDbContext>(options => options.UseNpgsql(connectionString));

builder.Services.AddHttpClient();

builder.Services.AddSingleton(_ => new DiscordSocketClient(new DiscordSocketConfig
{
	GatewayIntents = GatewayIntents.Guilds,
	AlwaysDownloadUsers = false
}));

builder.Services.AddSingleton(provider => new InteractionService(provider.GetRequiredService<DiscordSocketClient>()));

builder.Services.AddHostedService<DiscordGatewayHostedService>();
builder.Services.AddHostedService<InteractionHandlerHostedService>();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
