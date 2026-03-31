namespace DiscordXBot.Services.Models;

public enum PublishFailureKind
{
    None,
    ChannelMissing,
    RateLimited,
    Transient,
    Fatal
}

public sealed record PublishResult(bool Success, PublishFailureKind FailureKind, string? ErrorMessage = null)
{
    public static PublishResult Ok() => new(true, PublishFailureKind.None);
    public static PublishResult Fail(PublishFailureKind kind, string? errorMessage = null) => new(false, kind, errorMessage);
}
