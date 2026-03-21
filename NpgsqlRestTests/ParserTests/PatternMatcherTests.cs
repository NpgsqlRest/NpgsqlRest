namespace NpgsqlRestTests.ParserTests;

public class PatternMatcherTests
{
    [Theory]
    [InlineData("", "", false)]
    [InlineData("test", "", false)]
    [InlineData("", "test", false)]
    [InlineData(null, "test", false)]
    [InlineData("test", null, false)]
    [InlineData(null, null, false)]
    public void EdgeCases_EmptyAndNull_ReturnFalse(string? name, string? pattern, bool expected)
    {
        Parser.IsPatternMatch(name!, pattern!).Should().Be(expected);
    }

    [Theory]
    [InlineData("test", "test", true)]
    [InlineData("test", "TEST", true)]
    [InlineData("a", "b", false)]
    [InlineData("abc", "abcd", false)]
    public void ExactMatches_WithoutWildcards(string name, string pattern, bool expected)
    {
        Parser.IsPatternMatch(name, pattern).Should().Be(expected);
    }

    [Theory]
    [InlineData("test.txt", "*.txt", true)]
    [InlineData("test.doc", "*.txt", false)]
    [InlineData("file", "*", true)]
    [InlineData("", "*", false)]
    [InlineData("abc", "a*", true)]
    [InlineData("abc", "*c", true)]
    [InlineData("abc", "a*c", true)]
    [InlineData("abcdef", "*d*f", true)]
    [InlineData("abc", "*d", false)]
    public void StarWildcard_MatchesCorrectly(string name, string pattern, bool expected)
    {
        Parser.IsPatternMatch(name, pattern).Should().Be(expected);
    }

    [Theory]
    [InlineData("test", "t?st", true)]
    [InlineData("test", "te?t", true)]
    [InlineData("test", "????", true)]
    [InlineData("test", "tes?", true)]
    [InlineData("test", "?est", true)]
    [InlineData("abc", "a?c?", false)]
    [InlineData("abc", "??", false)]
    [InlineData("abc", "a?d", false)]
    public void QuestionMarkWildcard_MatchesCorrectly(string name, string pattern, bool expected)
    {
        Parser.IsPatternMatch(name, pattern).Should().Be(expected);
    }

    [Theory]
    [InlineData("testfile.txt", "t*t", true)]
    [InlineData("abcde", "a*c?e", true)]
    [InlineData("abcde", "*?e", true)]
    [InlineData("x", "*?*", true)]
    [InlineData("abcdef", "a*d?f", true)]
    [InlineData("a", "**", true)]
    [InlineData("abc", "a**c", true)]
    public void CombinedWildcards_MatchesCorrectly(string name, string pattern, bool expected)
    {
        Parser.IsPatternMatch(name, pattern).Should().Be(expected);
    }

    [Theory]
    [InlineData("verylongfilename.txt", "*.txt", true)]
    [InlineData("a", "************************a", true)]
    [InlineData("abc", "???", true)]
    [InlineData("special@#$%.txt", "*@#$%.txt", true)]
    public void ExtremeCases_MatchesCorrectly(string name, string pattern, bool expected)
    {
        Parser.IsPatternMatch(name, pattern).Should().Be(expected);
    }

    [Fact]
    public void PerformanceTest_LargeInput_DoesNotStackOverflow()
    {
        string largeName = new('a', 10000);
        string largePattern = "*" + new string('?', 9999);
        Assert.True(Parser.IsPatternMatch(largeName, largePattern));
    }

    [Theory]
    [InlineData("test", "TEST", true)]
    [InlineData("Test", "TEST", true)]
    [InlineData("test", "tEsT", true)]
    [InlineData("TEST", "t?st", true)]
    [InlineData("TEST", "t*st", true)]
    [InlineData("TEST", "*st", true)]
    public void CaseInsensitive_MatchesCorrectly(string name, string pattern, bool expected)
    {
        Parser.IsPatternMatch(name, pattern).Should().Be(expected);
    }

    // ** (double-star / recursive glob) tests

    [Theory]
    [InlineData("sql/dir/file.sql", "sql/**/*.sql", true)]
    [InlineData("sql/a/b/c/file.sql", "sql/**/*.sql", true)]
    [InlineData("sql/file.sql", "sql/**/*.sql", true)]
    [InlineData("sql/dir/file.txt", "sql/**/*.sql", false)]
    [InlineData("other/dir/file.sql", "sql/**/*.sql", false)]
    public void DoubleStar_RecursiveGlob_MatchesCorrectly(string name, string pattern, bool expected)
    {
        Parser.IsPatternMatch(name, pattern).Should().Be(expected);
    }

    [Theory]
    [InlineData("sql/file.sql", "sql/*.sql", true)]
    [InlineData("sql/dir/file.sql", "sql/*.sql", true)]        // no ** in pattern → * matches everything
    [InlineData("dir/sub/file.txt", "dir/*.txt", true)]         // no ** in pattern → * matches everything
    [InlineData("dir/file.txt", "dir/*.txt", true)]
    public void SingleStar_WithoutDoubleStar_CrossesSlash(string name, string pattern, bool expected)
    {
        Parser.IsPatternMatch(name, pattern).Should().Be(expected);
    }

    [Theory]
    [InlineData("file.sql", "**/*.sql", true)]
    [InlineData("a/file.sql", "**/*.sql", true)]
    [InlineData("a/b/c/file.sql", "**/*.sql", true)]
    [InlineData("a/b/c/file.txt", "**/*.sql", false)]
    public void DoubleStar_AtStart_MatchesAnyDepth(string name, string pattern, bool expected)
    {
        Parser.IsPatternMatch(name, pattern).Should().Be(expected);
    }

    [Theory]
    [InlineData("dir/file.sql", "dir/**/file.sql", true)]
    [InlineData("dir/a/file.sql", "dir/**/file.sql", true)]
    [InlineData("dir/a/b/file.sql", "dir/**/file.sql", true)]
    [InlineData("dir/a/b/other.sql", "dir/**/file.sql", false)]
    public void DoubleStar_InMiddle_MatchesAnyDepth(string name, string pattern, bool expected)
    {
        Parser.IsPatternMatch(name, pattern).Should().Be(expected);
    }

    [Theory]
    [InlineData("a/b/c", "**", true)]
    [InlineData("file.txt", "**", true)]
    [InlineData("a", "**", true)]
    public void DoubleStar_Alone_MatchesEverything(string name, string pattern, bool expected)
    {
        Parser.IsPatternMatch(name, pattern).Should().Be(expected);
    }

    [Theory]
    [InlineData("/admin/page.html", "/admin/*.html", true)]
    [InlineData("/admin/sub/page.html", "/admin/*.html", true)]     // no ** → * crosses /
    [InlineData("/admin/sub/page.html", "/admin/**/*.html", true)]  // ** crosses /
    [InlineData("image/png", "image/*", true)]                      // MIME type — no / in segment
    [InlineData("image/svg+xml", "image/*", true)]
    public void BackwardCompat_ExistingPatterns_StillWork(string name, string pattern, bool expected)
    {
        Parser.IsPatternMatch(name, pattern).Should().Be(expected);
    }
}
