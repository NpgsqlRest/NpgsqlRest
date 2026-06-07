using NpgsqlRest.Common;

namespace NpgsqlRestTests.ParserTests;

// Characterization tests pinning the behavior of the shared comment-parsing string primitives
// (NpgsqlRest.Common.CommentPrimitives) — extracted from DefaultCommentParser in MCP plan Phase 0.
// They document behavior, quirks included.
public class CommentPrimitivesTests
{
    // ---- StrEquals: optional leading '@' stripped from str1, then OrdinalIgnoreCase compare ----

    [Theory]
    [InlineData("authorize", "authorize", true)]
    [InlineData("@authorize", "authorize", true)]   // '@' prefix on str1 is stripped
    [InlineData("AUTHORIZE", "authorize", true)]     // case-insensitive
    [InlineData("authorize", "AUTHORIZE", true)]
    [InlineData("authorize", "auth", false)]
    [InlineData("authorize", "@authorize", false)]   // '@' is NOT stripped from str2
    [InlineData("@", "", true)]                       // "@" -> "" equals ""
    [InlineData("", "", true)]
    public void StrEquals_MatchesCurrentBehavior(string str1, string str2, bool expected)
    {
        CommentPrimitives.StrEquals(str1, str2).Should().Be(expected);
    }

    // ---- StrEqualsToArray: '@' stripped from str, then OrdinalIgnoreCase match against any ----

    [Fact]
    public void StrEqualsToArray_MatchesAnyAlias()
    {
        CommentPrimitives.StrEqualsToArray("cached", "cached", "cache").Should().BeTrue();
        CommentPrimitives.StrEqualsToArray("@cache", "cached", "cache").Should().BeTrue();
        CommentPrimitives.StrEqualsToArray("CACHE", "cached", "cache").Should().BeTrue();
    }

    [Fact]
    public void StrEqualsToArray_NoMatch_ReturnsFalse()
    {
        CommentPrimitives.StrEqualsToArray("x", "a", "b").Should().BeFalse();
    }

    [Fact]
    public void StrEqualsToArray_EmptyArray_ReturnsFalse()
    {
        CommentPrimitives.StrEqualsToArray("anything").Should().BeFalse();
    }

    // ---- SplitWords: split on space/comma, RemoveEmptyEntries, trim, preserve case ----

    [Fact]
    public void SplitWords_SplitsOnSpaceAndComma_Trimmed_PreservingCase()
    {
        "a, b, c".SplitWords().Should().Equal("a", "b", "c");
        "a  b".SplitWords().Should().Equal("a", "b");
        " a , , b ".SplitWords().Should().Equal("a", "b");   // empty entries removed
        "Foo Bar".SplitWords().Should().Equal("Foo", "Bar"); // case preserved
    }

    [Fact]
    public void SplitWords_NullOrEmpty_ReturnsEmpty()
    {
        ((string)null!).SplitWords().Should().BeEmpty();
        "".SplitWords().Should().BeEmpty();
    }

    // ---- SplitWordsLower: same as SplitWords but lowercased ----

    [Fact]
    public void SplitWordsLower_LowercasesEachWord()
    {
        "Foo, BAR".SplitWordsLower().Should().Equal("foo", "bar");
        "MixedCase Word".SplitWordsLower().Should().Equal("mixedcase", "word");
    }

    [Fact]
    public void SplitWordsLower_NullOrEmpty_ReturnsEmpty()
    {
        ((string)null!).SplitWordsLower().Should().BeEmpty();
        "".SplitWordsLower().Should().BeEmpty();
    }

    // ---- SplitBySeparatorChar: split on first separator; false if absent or part1 has invalid name char ----
    // Valid name chars: letter, digit, '-', '_', '@'. Anything else in part1 => not a key/value pair.

    [Theory]
    [InlineData("key = value", '=', true, "key", "value")]
    [InlineData("Content-Type: application/json", ':', true, "Content-Type", "application/json")] // '-' allowed
    [InlineData("@timeout = 30s", '=', true, "@timeout", "30s")]                                   // '@' allowed
    [InlineData("no separator here", '=', false, null, null)]                                       // no separator
    [InlineData("a b = value", '=', false, null, null)]                                             // space in part1 -> invalid
    [InlineData("key=", '=', true, "key", "")]                                                       // empty value
    [InlineData("=value", '=', true, "", "value")]                                                   // empty key
    public void SplitBySeparatorChar_MatchesCurrentBehavior(string input, char sep, bool expected, string? p1, string? p2)
    {
        var result = CommentPrimitives.SplitBySeparatorChar(input, sep, out var part1, out var part2);
        result.Should().Be(expected);
        if (expected)
        {
            part1.Should().Be(p1);
            part2.Should().Be(p2);
        }
    }
}
