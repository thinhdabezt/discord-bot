using System.Net;

namespace DiscordXBot.Services;

internal static class DiscordUrlSanitizer
{
    public static string? Sanitize(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return null;
        }

        var value = WebUtility.HtmlDecode(rawUrl).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value.StartsWith("//", StringComparison.Ordinal))
        {
            value = "https:" + value;
        }

        if (!TryCreateHttpUri(value, out var uri))
        {
            // Some feeds include raw spaces in URLs; percent-encode and retry.
            var escaped = value.Replace(" ", "%20", StringComparison.Ordinal);
            if (!TryCreateHttpUri(escaped, out uri))
            {
                return null;
            }
        }

        var absolute = uri.AbsoluteUri;
        if (absolute.Length > 2048)
        {
            return null;
        }

        if (!IsPublicHost(uri.Host))
        {
            return null;
        }

        return absolute;
    }

    private static bool IsPublicHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Discord embed URL validation rejects non-public hosts (e.g. docker service names).
        return host.Contains('.', StringComparison.Ordinal);
    }

    private static bool TryCreateHttpUri(string value, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed) &&
            (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
        {
            uri = parsed;
            return true;
        }

        uri = null!;
        return false;
    }
}