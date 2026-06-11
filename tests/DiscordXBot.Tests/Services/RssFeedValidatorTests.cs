using System.Net;
using DiscordXBot.Configuration;
using DiscordXBot.Services;
using Microsoft.Extensions.Options;

namespace DiscordXBot.Tests.Services;

public sealed class RssFeedValidatorTests
{
    [Fact]
    public async Task ValidateAsync_AcceptsRssWithItem()
    {
        var validator = CreateValidator("""
            <?xml version="1.0" encoding="utf-8"?>
            <rss version="2.0"><channel><title>Test</title><item><title>Hello</title><link>https://example.com/1</link></item></channel></rss>
            """);

        var result = await validator.ValidateAsync("https://example.com/rss.xml");

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_AcceptsAtomWithEntry()
    {
        var validator = CreateValidator("""
            <?xml version="1.0" encoding="utf-8"?>
            <feed xmlns="http://www.w3.org/2005/Atom"><title>Test</title><entry><id>1</id><title>Hello</title></entry></feed>
            """);

        var result = await validator.ValidateAsync("https://example.com/atom.xml");

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_RejectsHtmlErrorPage()
    {
        var validator = CreateValidator("<html><body>not a feed</body></html>");

        var result = await validator.ValidateAsync("https://example.com/rss.xml");

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_RejectsEmptyFeed()
    {
        var validator = CreateValidator("""
            <?xml version="1.0" encoding="utf-8"?>
            <rss version="2.0"><channel><title>Empty</title></channel></rss>
            """);

        var result = await validator.ValidateAsync("https://example.com/rss.xml");

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_RejectsNonXmlResponse()
    {
        var validator = CreateValidator("plain text");

        var result = await validator.ValidateAsync("https://example.com/rss.xml");

        Assert.False(result.IsValid);
    }

    private static RssFeedValidator CreateValidator(string body)
    {
        var client = new HttpClient(new StaticResponseHandler(body))
        {
            BaseAddress = new Uri("https://example.com")
        };

        return new RssFeedValidator(
            new StaticHttpClientFactory(client),
            Options.Create(new RetryOptions { MaxRetries = 0, InitialDelaySeconds = 1 }));
    }

    private sealed class StaticHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return client;
        }
    }

    private sealed class StaticResponseHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            });
        }
    }
}
