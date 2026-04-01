namespace DiscordXBot.Services.Models;

public enum FeedFetchOutcome
{
    Success = 0,
    Empty = 1,
    ErrorOnly = 2,
    HttpForbidden = 3,
    HttpError = 4,
    NetworkError = 5,
    ParseError = 6
}

public sealed record FeedFetchResult(
    IReadOnlyList<RssPost> Posts,
    FeedFetchOutcome Outcome,
    int? StatusCode = null,
    string? Detail = null)
{
    public static FeedFetchResult Success(IReadOnlyList<RssPost> posts)
    {
        return new FeedFetchResult(posts, FeedFetchOutcome.Success);
    }

    public static FeedFetchResult Empty(string? detail = null)
    {
        return new FeedFetchResult([], FeedFetchOutcome.Empty, Detail: detail);
    }

    public static FeedFetchResult ErrorOnly(string? detail = null)
    {
        return new FeedFetchResult([], FeedFetchOutcome.ErrorOnly, Detail: detail);
    }

    public static FeedFetchResult HttpForbidden(string? detail = null)
    {
        return new FeedFetchResult([], FeedFetchOutcome.HttpForbidden, StatusCode: 403, Detail: detail);
    }

    public static FeedFetchResult HttpError(int statusCode, string? detail = null)
    {
        return new FeedFetchResult([], FeedFetchOutcome.HttpError, StatusCode: statusCode, Detail: detail);
    }

    public static FeedFetchResult NetworkError(string? detail = null)
    {
        return new FeedFetchResult([], FeedFetchOutcome.NetworkError, Detail: detail);
    }

    public static FeedFetchResult ParseError(string? detail = null)
    {
        return new FeedFetchResult([], FeedFetchOutcome.ParseError, Detail: detail);
    }
}
