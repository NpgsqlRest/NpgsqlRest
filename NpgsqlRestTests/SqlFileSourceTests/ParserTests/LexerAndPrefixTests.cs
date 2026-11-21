using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

/// <summary>
/// The SQL file lexer: quoted identifiers, escape strings and dollar signs (4.1.0), and the RequireAtPrefix
/// reading of the lines the source reads itself.
/// </summary>
public class LexerAndPrefixTests
{
    [Fact]
    public void A_quoted_identifier_may_contain_a_comment_marker()
    {
        var result = SqlFileParser.Parse("-- HTTP GET\nselect 1 as \"a--b\", 2 as c;");
        result.Errors.Should().BeEmpty();
        result.Statements.Should().Equal("select 1 as \"a--b\", 2 as c");
        result.Comment.Trim().Should().Be("HTTP GET");
    }

    [Fact]
    public void A_quoted_identifier_may_contain_a_semicolon_and_quotes()
    {
        var result = SqlFileParser.Parse("select 1 as \"a;b\", 2 as \"say \"\"hi\"\"; ok\", 3 as \"it's /* not a comment */\";\nselect 2;");
        result.Statements.Should().Equal(
            "select 1 as \"a;b\", 2 as \"say \"\"hi\"\"; ok\", 3 as \"it's /* not a comment */\"",
            "select 2");
        result.Comment.Should().BeEmpty();
    }

    [Fact]
    public void An_unterminated_quoted_identifier_takes_the_rest_of_the_file()
    {
        var result = SqlFileParser.Parse("select 1 as \"open; select 2;");
        result.Statements.Should().Equal("select 1 as \"open; select 2;");
    }

    [Fact]
    public void Two_parameters_side_by_side_are_not_a_dollar_quoted_string()
    {
        var result = SqlFileParser.Parse("select $1$2 as glued; -- @single\nselect $2;");
        result.Statements.Should().Equal("select $1$2 as glued", "select $2");
        result.SingleCommands.Should().Equal(0);
    }

    [Theory]
    [InlineData("select $$a;b$$ as v;\nselect 2;", "select $$a;b$$ as v")]
    [InlineData("select $tag$a;b -- x$tag$ as v;\nselect 2;", "select $tag$a;b -- x$tag$ as v")]
    [InlineData("select $_t1$a;b$_t1$ as v;\nselect 2;", "select $_t1$a;b$_t1$ as v")]
    public void Dollar_quoted_strings_are_still_strings(string sql, string first)
    {
        var result = SqlFileParser.Parse(sql);
        result.Statements.Should().Equal(first, "select 2");
        result.Comment.Should().BeEmpty();
    }

    [Fact]
    public void A_backslash_escapes_a_quote_in_an_escape_string()
    {
        var result = SqlFileParser.Parse("select E'it\\'s; here' as a, e'\\\\' as b;\nselect 2;");
        result.Statements.Should().Equal("select E'it\\'s; here' as a, e'\\\\' as b", "select 2");
    }

    [Fact]
    public void A_backslash_is_an_ordinary_character_in_a_plain_string()
    {
        var result = SqlFileParser.Parse("select 'c:\\' as a, name'x\\' as b;\nselect 2;");
        result.Statements.Should().Equal("select 'c:\\' as a, name'x\\' as b", "select 2");
    }

    [Theory]
    [InlineData("-- single\nselect 1;")]
    [InlineData("-- skip\nselect 1;")]
    [InlineData("-- returns text\nselect 1;")]
    [InlineData("-- result first\nselect 1;")]
    [InlineData("-- define_param _x text\nselect 1;")]
    public void With_the_prefix_required_a_line_without_it_is_text(string sql)
    {
        var result = SqlFileParser.Parse(sql, CommentScope.All, requireAtPrefix: true);
        result.SingleCommands.Should().BeEmpty();
        result.SkipCommands.Should().BeEmpty();
        result.ReturnsTypeOverrides.Should().BeEmpty();
        result.PositionalResultNames.Should().BeEmpty();
        result.VirtualParams.Should().BeEmpty();
        result.ConsumedCommentLines.Should().BeEmpty();
    }

    [Fact]
    public void With_the_prefix_required_the_prefixed_lines_are_read()
    {
        var result = SqlFileParser.Parse(
            "-- @define_param _x text\n-- @returns my_type\nselect 1; -- @single\n-- @skip\nselect 2;\n-- @result third\nselect 3;",
            CommentScope.All, requireAtPrefix: true);
        result.VirtualParams.Should().Equal(("_x", "text"));
        result.ReturnsTypeOverrides.Should().Equal(new Dictionary<int, string> { [0] = "my_type" });
        result.SingleCommands.Should().Equal(0);
        result.SkipCommands.Should().Equal(1);
        result.PositionalResultNames.Should().Equal(new Dictionary<int, string> { [2] = "third" });
    }

    [Fact]
    public void Without_the_setting_both_forms_are_read()
    {
        var result = SqlFileParser.Parse("-- single\nselect 1;\n-- @single\nselect 2;");
        result.SingleCommands.Should().BeEquivalentTo([0, 1]);
    }

    [Theory]
    [InlineData("param $1 type is integer", false, "integer")]
    [InlineData("@param $1 type is integer", false, "integer")]
    [InlineData("@param $1 type is integer", true, "integer")]
    [InlineData("param $1 type is integer", true, null)]
    public void Parameter_type_hints_follow_the_setting(string comment, bool requireAtPrefix, string? expected)
    {
        var hints = SqlFileParser.ExtractParamTypeHints(comment, null, requireAtPrefix);
        if (expected is null)
        {
            hints.Should().BeNull();
        }
        else
        {
            hints![0].Should().Be(expected);
        }
    }
}
