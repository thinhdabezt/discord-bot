using DiscordXBot.Discord.Commands;

namespace DiscordXBot.Tests.Discord.Commands;

public class InstagramFeedModuleTests
{
    [Theory]
    [InlineData("nasa", "nasa")]
    [InlineData("@nasa", "nasa")]
    [InlineData("NASA.Space", "nasa.space")]
    [InlineData("https://www.instagram.com/nasa/", "nasa")]
    [InlineData("https://instagram.com/nasa?igsh=abc", "nasa")]
    public void NormalizeInstagramUsername_AcceptsUsernameForms(string input, string expected)
    {
        Assert.Equal(expected, InstagramFeedModule.NormalizeInstagramUsername(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://x.com/nasa")]
    [InlineData("https://www.instagram.com/p/example")]
    [InlineData("https://www.instagram.com/reel/example")]
    [InlineData("this_username_is_longer_than_thirty_chars")]
    public void NormalizeInstagramUsername_RejectsUnsupportedForms(string input)
    {
        Assert.Equal(string.Empty, InstagramFeedModule.NormalizeInstagramUsername(input));
    }
}
