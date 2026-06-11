namespace DiscordXBot.Services.Models;

public sealed record RssFeedValidationResult(bool IsValid, string Reason)
{
    public static RssFeedValidationResult Valid()
    {
        return new RssFeedValidationResult(true, string.Empty);
    }

    public static RssFeedValidationResult Invalid(string reason)
    {
        return new RssFeedValidationResult(false, reason);
    }
}
