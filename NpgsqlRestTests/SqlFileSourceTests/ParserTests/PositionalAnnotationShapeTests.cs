using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

/// <summary>
/// The SQL file source reads four annotations of its own from comments: returns, result, skip and define_param.
/// A comment may also simply be a sentence that begins with one of those words. A line is an annotation when its
/// first WORD is the keyword, and - without the @ prefix - only when the rest has the shape of one.
/// </summary>
public class PositionalAnnotationShapeTests
{
    private static SqlFileParseResult Parse(string header, string statements = "select 1;") =>
        SqlFileParser.Parse(header + "\n" + statements);

    [Theory]
    [InlineData("-- Returns all active users")]
    [InlineData("-- returns the rows that match")]
    [InlineData("-- Returns: a list of users")]
    [InlineData("/* Returns one row per customer */")]
    [InlineData("-- returns")]
    [InlineData("-- returns_all users")]
    [InlineData("-- returnsusers")]
    public void A_sentence_starting_with_returns_is_not_the_annotation(string header)
    {
        var result = Parse(header);
        result.ReturnsTypeOverrides.Should().BeEmpty();
        result.ImplicitReturns.Should().BeEmpty();
        result.ConsumedCommentLines.Should().BeEmpty();
        result.Statements.Should().Equal("select 1");
    }

    [Theory]
    [InlineData("-- returns users", "users")]
    [InlineData("-- RETURNS my_schema.user_row", "my_schema.user_row")]
    [InlineData("-- returns int[]", "int[]")]
    [InlineData("-- returns numeric(10, 2)", "numeric(10, 2)")]
    [InlineData("-- returns double precision", "double precision")]
    [InlineData("-- returns timestamp with time zone", "timestamp with time zone")]
    [InlineData("-- returns character varying(20)", "character varying(20)")]
    [InlineData("-- returns \"My Type\"", "\"My Type\"")]
    [InlineData("-- returns void;", "void")]
    public void Without_the_prefix_a_type_shaped_value_is_the_annotation_and_marked_implicit(string header, string type)
    {
        var result = Parse(header);
        result.ReturnsTypeOverrides.Should().Equal(new Dictionary<int, string> { [0] = type });
        result.ImplicitReturns.Should().Equal(0);
        result.ConsumedCommentLines.Should().ContainSingle();
    }

    [Theory]
    [InlineData("-- @returns users", "users")]
    [InlineData("-- @returns my composite type", "my composite type")]
    [InlineData("-- @RETURNS void", "void")]
    public void With_the_prefix_the_line_is_always_the_annotation_and_explicit(string header, string type)
    {
        var result = Parse(header);
        result.ReturnsTypeOverrides.Should().Equal(new Dictionary<int, string> { [0] = type });
        result.ImplicitReturns.Should().BeEmpty();
    }

    [Theory]
    [InlineData("-- result of the join")]
    [InlineData("-- result is not what you expect")]
    [InlineData("-- resulting rows")]
    [InlineData("-- results users")]
    [InlineData("-- result")]
    [InlineData("-- result user-list")]
    public void A_sentence_starting_with_result_is_not_the_annotation(string comment)
    {
        var result = SqlFileParser.Parse("select 1;\n" + comment + "\nselect 2;");
        result.PositionalResultNames.Should().BeEmpty();
        result.ConsumedCommentLines.Should().BeEmpty();
    }

    [Theory]
    [InlineData("-- result users", "users")]
    [InlineData("-- result is users", "users")]
    [InlineData("-- RESULT IS user_list", "user_list")]
    [InlineData("-- @result users", "users")]
    [InlineData("-- @result is users", "users")]
    // with the prefix the line is meant as the annotation: the first word is the name, as before
    [InlineData("-- @result of the join", "of")]
    public void Result_names_the_next_statement(string comment, string name)
    {
        var result = SqlFileParser.Parse("select 1;\n" + comment + "\nselect 2;");
        result.PositionalResultNames.Should().Equal(new Dictionary<int, string> { [1] = name });
        result.ConsumedCommentLines.Should().ContainSingle();
    }

    [Theory]
    [InlineData("-- define_params tenant")]
    [InlineData("-- define_parameter tenant")]
    [InlineData("-- define_param")]
    [InlineData("-- @define_param")]
    public void A_line_that_is_not_define_param_defines_nothing(string header)
    {
        Parse(header).VirtualParams.Should().BeEmpty();
    }

    [Theory]
    [InlineData("-- define_param tenant", "tenant", null)]
    [InlineData("-- @define_param tenant integer", "tenant", "integer")]
    [InlineData("-- @DEFINE_PARAM   tenant   uuid   ignored", "tenant", "uuid")]
    public void Define_param_defines_a_virtual_parameter(string header, string name, string? type)
    {
        var result = Parse(header);
        result.VirtualParams.Should().Equal((name, type));
        result.ConsumedCommentLines.Should().ContainSingle();
    }

    [Fact]
    public void Header_of_a_single_statement_without_a_semicolon_is_read()
    {
        // The header was only scanned when the statement ended with a semicolon.
        var result = SqlFileParser.Parse("-- HTTP GET\n-- @returns void\n-- @single\nselect 1");
        result.Statements.Should().Equal("select 1");
        result.ReturnsTypeOverrides.Should().Equal(new Dictionary<int, string> { [0] = "void" });
        result.SingleCommands.Should().Equal(0);
    }

    [Fact]
    public void Skip_and_single_are_whole_words()
    {
        var result = SqlFileParser.Parse("select 1;\n-- skip this for now\n-- singles only\nselect 2;\n-- @skip\nselect 3;\n-- single\nselect 4;");
        result.SkipCommands.Should().Equal(2);
        result.SingleCommands.Should().Equal(3);
        // skip is the source's own line; single is also core's (the endpoint as a whole), so it is left for core
        result.ConsumedCommentLines.Should().Equal("@skip");
    }

    [Fact]
    public void Inline_annotation_after_the_semicolon_applies_to_that_statement()
    {
        var result = SqlFileParser.Parse("select 1; -- @result first\nselect 2; -- result is second\nselect 3; -- result of nothing");
        result.PositionalResultNames.Should().Equal(new Dictionary<int, string> { [0] = "first", [1] = "second" });
    }
}
