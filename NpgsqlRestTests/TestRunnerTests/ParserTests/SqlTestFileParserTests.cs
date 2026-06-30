using NpgsqlRestClient.Testing;

namespace NpgsqlRestTests.TestRunnerTests.ParserTests;

public class SqlTestFileParserTests
{
    [Fact]
    public void Single_Statement_Yields_One_Sql_Step()
    {
        var steps = SqlTestFileParser.Parse("select 1;");
        steps.Should().ContainSingle();
        var sql = steps[0].Should().BeOfType<SqlStep>().Subject;
        sql.Text.Should().Be("select 1");
        sql.IsDoBlock.Should().BeFalse();
    }

    [Fact]
    public void Two_Statements_Split_On_Semicolon()
    {
        var steps = SqlTestFileParser.Parse("select 1;\nselect 2;");
        steps.Should().HaveCount(2);
        steps.Should().AllBeOfType<SqlStep>();
        ((SqlStep)steps[0]).Text.Should().Be("select 1");
        ((SqlStep)steps[1]).Text.Should().Be("select 2");
    }

    [Fact]
    public void Trailing_Statement_Without_Semicolon_Is_Flushed()
    {
        var steps = SqlTestFileParser.Parse("select 1;\nselect 2");
        steps.Should().HaveCount(2);
        ((SqlStep)steps[1]).Text.Should().Be("select 2");
    }

    [Fact]
    public void Do_Block_Is_A_Single_Step_With_Inner_Semicolons_Kept()
    {
        var sqlText = "do $$\nbegin\n  insert into t values (1);\n  assert true, 'x';\nend;\n$$;";
        var steps = SqlTestFileParser.Parse(sqlText);
        steps.Should().ContainSingle();
        var sql = (SqlStep)steps[0];
        sql.IsDoBlock.Should().BeTrue();
        sql.Text.Should().Contain("insert into t values (1)");
        sql.Text.Should().Contain("assert true");
    }

    [Fact]
    public void Semicolon_Inside_Single_Quote_Does_Not_Split()
    {
        var steps = SqlTestFileParser.Parse("select 'a;b' as v;");
        steps.Should().ContainSingle();
        ((SqlStep)steps[0]).Text.Should().Be("select 'a;b' as v");
    }

    [Fact]
    public void Escaped_Quote_Inside_String_Is_Handled()
    {
        var steps = SqlTestFileParser.Parse("select 'O''Reilly; co' as v;");
        steps.Should().ContainSingle();
        ((SqlStep)steps[0]).Text.Should().Be("select 'O''Reilly; co' as v");
    }

    [Fact]
    public void Tagged_Dollar_Quote_With_Inner_Semicolon()
    {
        var steps = SqlTestFileParser.Parse("select $tag$ a; b $tag$ as v;");
        steps.Should().ContainSingle();
        ((SqlStep)steps[0]).Text.Should().Contain("$tag$ a; b $tag$");
    }

    [Fact]
    public void Line_Comments_Are_Ignored()
    {
        var steps = SqlTestFileParser.Parse("-- a note\nselect 1; -- trailing\nselect 2;");
        steps.Should().HaveCount(2);
    }

    [Fact]
    public void NonHttp_Block_Comment_Is_Ignored()
    {
        var steps = SqlTestFileParser.Parse("/* just a note */\nselect 1;");
        steps.Should().ContainSingle().Which.Should().BeOfType<SqlStep>();
    }

    [Fact]
    public void Http_Block_Between_Statements_Preserves_Order()
    {
        var sqlText =
            "insert into t values (1);\n" +
            "/*\nGET /x\n*/\n" +
            "do $$ begin assert (select status from _response) = 200; end $$;";
        var steps = SqlTestFileParser.Parse(sqlText);
        steps.Should().HaveCount(3);
        steps[0].Should().BeOfType<SqlStep>();
        steps[1].Should().BeOfType<HttpStep>();
        steps[2].Should().BeOfType<SqlStep>();
        var http = (HttpStep)steps[1];
        http.Method.Should().Be("GET");
        http.Path.Should().Be("/x");
    }

    [Fact]
    public void Http_Block_First_Yields_Http_Step_First()
    {
        var steps = SqlTestFileParser.Parse("/*\nPOST /things\n\n{\"a\":1}\n*/\nselect 1;");
        steps.Should().HaveCount(2);
        steps[0].Should().BeOfType<HttpStep>();
        ((HttpStep)steps[0]).Body.Should().Be("{\"a\":1}");
        steps[1].Should().BeOfType<SqlStep>();
    }

    [Fact]
    public void Line_Numbers_Are_Tracked()
    {
        var sqlText = "select 1;\n\n/*\nGET /x\n*/\nselect 2;";
        var steps = SqlTestFileParser.Parse(sqlText);
        steps.Should().HaveCount(3);
        steps[0].LineNumber.Should().Be(1);   // select 1
        steps[1].LineNumber.Should().Be(3);   // /* opens on line 3
        steps[2].LineNumber.Should().Be(6);   // select 2
    }

    [Fact]
    public void Block_Comment_In_Middle_Of_Statement_Is_Stripped()
    {
        var steps = SqlTestFileParser.Parse("select /* note */ 1;");
        steps.Should().ContainSingle();
        ((SqlStep)steps[0]).Text.Should().StartWith("select");
        ((SqlStep)steps[0]).Text.Should().EndWith("1");
    }
}
