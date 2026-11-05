using Broiler.Mail.Application.Preview;

namespace Broiler.Mail.Tests;

public sealed class MessageTextFinderTests
{
    [Theory]
    [InlineData("Alpha beta ALPHA", "alpha", 2)]
    [InlineData("Grüße 😀 Grüße", "GRÜßE", 2)]
    [InlineData("aaa", "aa", 1)]
    [InlineData("100% _ [x] .*", ".*", 1)]
    [InlineData("é cafe", "e", 1)]
    [InlineData("", "none", 0)]
    public void LiteralUnicodeCaseMatchingIsBoundedAndNonOverlapping(string text, string query, int count)
    {
        var result = MessageTextFinder.Find(text, query); Assert.Equal(count, result.Matches.Count); Assert.False(result.HasMore);
        foreach (var match in result.Matches) Assert.Equal(query, text.Substring(match.Start, match.Length), ignoreCase: true);
    }
    [Theory]
    [InlineData("")]
    [InlineData("bad\nquery")]
    public void InvalidQueryIsRefused(string query) => Assert.Throws<ArgumentException>(() => MessageTextFinder.Find("Body", query));
    [Fact]
    public void InvalidUnicodeQueryIsRefused() => Assert.Throws<ArgumentException>(() => MessageTextFinder.Find("Body", new string((char)0xd800, 1)));
    [Fact]
    public void HitCapAndInputLimitsAreExplicit()
    {
        var result = MessageTextFinder.Find(new string('a', 2000), "a"); Assert.Equal(1000, result.Matches.Count); Assert.True(result.HasMore);
        Assert.Throws<ArgumentException>(() => MessageTextFinder.Find("Body", new string('a', 257)));
        Assert.Throws<ArgumentException>(() => MessageTextFinder.Find(new string('a', MessageTextFinder.MaximumTextLength + 1), "a"));
        Assert.IsAssignableFrom<System.Collections.IList>(result.Matches); Assert.True(((System.Collections.IList)result.Matches).IsReadOnly);
    }
}
