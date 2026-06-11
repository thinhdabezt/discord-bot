using System.ServiceModel.Syndication;
using System.Xml;
using DiscordXBot.Configuration;
using DiscordXBot.Services.Models;
using Microsoft.Extensions.Options;

namespace DiscordXBot.Services;

public sealed class RssFeedValidator(
    IHttpClientFactory httpClientFactory,
    IOptions<RetryOptions> retryOptions)
{
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly IOptions<RetryOptions> _retryOptions = retryOptions;

    public async Task<RssFeedValidationResult> ValidateAsync(string rssUrl, CancellationToken cancellationToken = default)
    {
        var maxRetries = Math.Max(0, _retryOptions.Value.MaxRetries);
        var initialDelaySeconds = Math.Max(1, _retryOptions.Value.InitialDelaySeconds);

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(8));

                var client = _httpClientFactory.CreateClient();
                using var response = await client.GetAsync(rssUrl, HttpCompletionOption.ResponseContentRead, timeoutCts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    if ((int)response.StatusCode >= 500 && attempt < maxRetries)
                    {
                        await DelayRetryAsync(initialDelaySeconds, attempt, cancellationToken);
                        continue;
                    }

                    return RssFeedValidationResult.Invalid($"RSS URL returned HTTP {(int)response.StatusCode}.");
                }

                var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
                return ValidateXmlBody(body);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < maxRetries)
            {
                await DelayRetryAsync(initialDelaySeconds, attempt, cancellationToken);
            }
            catch (HttpRequestException) when (attempt < maxRetries)
            {
                await DelayRetryAsync(initialDelaySeconds, attempt, cancellationToken);
            }
            catch (Exception ex) when (ex is XmlException or InvalidOperationException)
            {
                return RssFeedValidationResult.Invalid("RSS URL did not return parseable RSS/Atom XML.");
            }
            catch
            {
                return RssFeedValidationResult.Invalid("Unable to validate this RSS URL.");
            }
        }

        return RssFeedValidationResult.Invalid("Unable to validate this RSS URL.");
    }

    private static RssFeedValidationResult ValidateXmlBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return RssFeedValidationResult.Invalid("RSS URL returned an empty response.");
        }

        using var textReader = new StringReader(body);
        using var xmlReader = XmlReader.Create(textReader, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
        var feed = SyndicationFeed.Load(xmlReader);
        if (feed?.Items is null)
        {
            return RssFeedValidationResult.Invalid("RSS URL did not contain RSS/Atom items.");
        }

        foreach (var item in feed.Items)
        {
            if (!string.IsNullOrWhiteSpace(item.Id) ||
                !string.IsNullOrWhiteSpace(item.Title?.Text) ||
                item.Links.Count > 0 ||
                !string.IsNullOrWhiteSpace(item.Summary?.Text))
            {
                return RssFeedValidationResult.Valid();
            }
        }

        return RssFeedValidationResult.Invalid("RSS URL did not contain any usable items.");
    }

    private static Task DelayRetryAsync(int initialDelaySeconds, int attempt, CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(initialDelaySeconds * Math.Pow(2, attempt));
        return Task.Delay(delay, cancellationToken);
    }
}
