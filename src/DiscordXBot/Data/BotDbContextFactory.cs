using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace DiscordXBot.Data;

public sealed class BotDbContextFactory : IDesignTimeDbContextFactory<BotDbContext>
{
    public BotDbContext CreateDbContext(string[] args)
    {
        var basePath = Directory.GetCurrentDirectory();

        var config = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = config.GetConnectionString("Default")
            ?? "Host=db.rmaitvguorarjzaifgkc.supabase.co;Port=5432;Database=postgres;Username=postgres;Password=Jm8jZRnFbe5Pi6sq;SSL Mode=Require;Trust Server Certificate=true";

        var optionsBuilder = new DbContextOptionsBuilder<BotDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new BotDbContext(optionsBuilder.Options);
    }
}
