using NpgsqlRestClient.Testing;

namespace NpgsqlRestTests.TestRunnerTests.ParserTests;

public class TestFileHeaderTests
{
    [Fact]
    public void Header_Parses_Setup_Teardown_And_Connection()
    {
        var h = TestFileHeader.Parse("-- @setup CreateDb ApplyMigrations\n-- @teardown DropDb\n-- @connection Isolated\n\nselect 1;");
        h.Setup.Should().Equal("CreateDb", "ApplyMigrations");
        h.Teardown.Should().Equal("DropDb");
        h.ConnectionName.Should().Be("Isolated");
    }

    [Fact]
    public void Header_Annotations_Are_Repeatable_And_Case_Insensitive()
    {
        var h = TestFileHeader.Parse("-- @SETUP A\n-- @Setup B\nselect 1;");
        h.Setup.Should().Equal("A", "B");
    }

    [Fact]
    public void Names_May_Be_Separated_By_Whitespace_Or_Commas()
    {
        var h = TestFileHeader.Parse("-- @setup A, B\n-- @setup C,D  E\n-- @teardown X , Y\nselect 1;");
        h.Setup.Should().Equal("A", "B", "C", "D", "E");
        h.Teardown.Should().Equal("X", "Y");
    }

    [Fact]
    public void Header_Scan_Stops_At_First_Statement()
    {
        var h = TestFileHeader.Parse("-- @setup A\nselect 1;\n-- @setup B");
        h.Setup.Should().Equal("A");
    }

    [Fact]
    public void Plain_Comments_And_Blank_Lines_Do_Not_End_The_Header()
    {
        var h = TestFileHeader.Parse("-- Test: something.\n\n-- more docs\n-- @teardown Drop\nselect 1;");
        h.Teardown.Should().Equal("Drop");
    }

    [Fact]
    public void Unknown_Annotations_And_Bare_Keywords_Are_Ignored()
    {
        var h = TestFileHeader.Parse("-- @author me\n-- @setup\n-- @setup X\nselect 1;");
        h.Setup.Should().Equal("X");
        h.Teardown.Should().BeEmpty();
        h.ConnectionName.Should().BeNull();
    }

    [Fact]
    public void Tags_Are_Parsed_With_Whitespace_Or_Commas()
    {
        var h = TestFileHeader.Parse("-- @tag smoke, regression\n-- @tag slow\nselect 1;");
        h.Tags.Should().Equal("smoke", "regression", "slow");
    }
}

public class TagFilterTests
{
    [Theory]
    [InlineData(new[] { "smoke" }, new string[0], new string[0], true)]                 // no filters => runs
    [InlineData(new[] { "smoke" }, new[] { "smoke" }, new string[0], true)]             // include hit
    [InlineData(new[] { "smoke" }, new[] { "SMOKE" }, new string[0], true)]             // case-insensitive
    [InlineData(new string[0], new[] { "smoke" }, new string[0], false)]                // include set, file untagged
    [InlineData(new[] { "smoke", "slow" }, new string[0], new[] { "slow" }, false)]     // exclude hit
    [InlineData(new[] { "smoke", "slow" }, new[] { "smoke" }, new[] { "slow" }, false)] // exclude wins over include
    public void Tag_Filter_Matches(string[] fileTags, string[] include, string[] exclude, bool expected)
    {
        NpgsqlRestClient.Testing.TestRunner.MatchesTags(
            fileTags,
            new HashSet<string>(include, StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(exclude, StringComparer.OrdinalIgnoreCase)).Should().Be(expected);
    }
}

public class FilterMatchTests
{
    [Theory]
    [InlineData("tests/login_succeeds.test.sql", "login", true)]                  // no wildcard => substring
    [InlineData("tests/login_succeeds.test.sql", "LOGIN", true)]                  // substring is case-insensitive
    [InlineData("tests/get_users.test.sql", "login", false)]
    [InlineData("tests/login_succeeds.test.sql", "*login*", true)]                // wildcard => glob
    [InlineData("tests/sub/deep.test.sql", "tests/sub/*", true)]
    [InlineData("tests\\win\\style.test.sql", "tests/win/*", true)]               // separators normalized
    public void Filter_Matches_Substring_Or_Glob(string path, string filter, bool expected)
    {
        NpgsqlRestClient.Testing.TestRunner.MatchesFilter(path, filter).Should().Be(expected);
    }
}

public class IncludeParseTests
{
    [Fact]
    public void Include_Lines_Are_Recognized_Between_Statements()
    {
        var steps = SqlTestFileParser.Parse("begin;\n\\ir fixtures/users.sql\n\\i ./global/seed.sql\nselect 1;\nrollback;");
        steps.Should().HaveCount(5);
        steps[1].Should().BeOfType<IncludeStep>().Which.RelativeToFile.Should().BeTrue();
        ((IncludeStep)steps[1]).Path.Should().Be("fixtures/users.sql");
        steps[2].Should().BeOfType<IncludeStep>().Which.RelativeToFile.Should().BeFalse();
        ((IncludeStep)steps[2]).Path.Should().Be("./global/seed.sql");
    }

    [Fact]
    public void Quoted_Path_And_Trailing_Semicolon_Are_Handled()
    {
        var steps = SqlTestFileParser.Parse("\\ir 'my fixtures/data.sql';\n");
        steps.Should().ContainSingle().Which.Should().BeOfType<IncludeStep>()
            .Which.Path.Should().Be("my fixtures/data.sql");
    }

    [Fact]
    public void Backslash_Inside_A_Statement_Or_String_Is_Not_An_Include()
    {
        var steps = SqlTestFileParser.Parse("select '\\ir not-an-include.sql';");
        steps.Should().ContainSingle().Which.Should().BeOfType<SqlStep>()
            .Which.Text.Should().Contain("\\ir not-an-include.sql");
    }

    [Fact]
    public void Unrecognized_Backslash_Line_Stays_Sql_Text()
    {
        var steps = SqlTestFileParser.Parse("\\echo hello;\nselect 1;");
        steps.Should().HaveCount(2);
        steps[0].Should().BeOfType<SqlStep>().Which.Text.Should().StartWith("\\echo");
    }
}

public class IncludeExpansionTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("npgsqlrest-include-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Write(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Included_Statements_Are_Spliced_In_Place_With_Source_Attribution()
    {
        var inc = Write("fixtures/users.sql", "insert into t values (1);\ninsert into t values (2);");
        var main = Write("main.test.sql", "begin;\n\\ir fixtures/users.sql\nselect true, 'ok';\nrollback;");

        var steps = TestFileLoader.LoadSteps(File.ReadAllText(main), main);

        steps.Should().HaveCount(5);
        steps[0].SourceFile.Should().BeNull();                       // begin (main file)
        steps[1].SourceFile.Should().Be(inc);                        // spliced insert 1
        steps[2].SourceFile.Should().Be(inc);                        // spliced insert 2
        ((SqlStep)steps[1]).Text.Should().Contain("values (1)");
        steps[3].SourceFile.Should().BeNull();                       // the assertion (main file)
    }

    [Fact]
    public void Nested_Includes_Resolve_Relative_To_The_Including_File()
    {
        Write("a/inner.sql", "select 'inner';");
        Write("a/outer.sql", "\\ir inner.sql");
        var main = Write("main.test.sql", "\\ir a/outer.sql");

        var steps = TestFileLoader.LoadSteps(File.ReadAllText(main), main);

        steps.Should().ContainSingle().Which.Should().BeOfType<SqlStep>()
            .Which.Text.Should().Contain("inner");
    }

    [Fact]
    public void Circular_Include_Is_Reported()
    {
        Write("x.sql", "\\ir y.sql");
        Write("y.sql", "\\ir x.sql");
        var main = Write("main.test.sql", "\\ir x.sql");

        var act = () => TestFileLoader.LoadSteps(File.ReadAllText(main), main);
        act.Should().Throw<InvalidOperationException>().WithMessage("*circular include*");
    }

    [Fact]
    public void Missing_Include_Is_Reported_With_Path_And_Line()
    {
        var main = Write("main.test.sql", "select 1;\n\\ir nope.sql");
        var act = () => TestFileLoader.LoadSteps(File.ReadAllText(main), main);
        act.Should().Throw<InvalidOperationException>().WithMessage("*include not found*nope.sql*");
    }

    [Fact]
    public void Http_Blocks_In_Included_Files_Are_Spliced_Like_Everything_Else()
    {
        var inc = Write("shared/login_call.sql", "/*\nPOST /api/login\n# @response shared_login\n\n{\"email\": \"x\"}\n*/");
        var main = Write("main.test.sql", "begin;\n\\ir shared/login_call.sql\nselect true, 'ok';\nrollback;");

        var steps = TestFileLoader.LoadSteps(File.ReadAllText(main), main);

        steps.Should().HaveCount(4);
        var http = steps[1].Should().BeOfType<HttpStep>().Subject;
        http.SourceFile.Should().Be(inc);
        http.Method.Should().Be("POST");
        http.ResponseTable.Should().Be("shared_login");
    }

    [Fact]
    public void Header_Annotations_In_Included_Files_Count_As_If_Pasted()
    {
        // A shared "profile": annotations only — the header continues in the host after it.
        Write("shared/iso_profile.sql", "-- shared isolation profile\n-- @setup CreateIso\n-- @teardown DropIso\n-- @connection Isolated\n");
        var main = Write("main.test.sql", "-- docs\n\\ir shared/iso_profile.sql\n-- @setup HostStep\nselect 1;");

        var h = TestFileHeader.Parse(File.ReadAllText(main), main);

        h.Setup.Should().Equal("CreateIso", "HostStep");
        h.Teardown.Should().Equal("DropIso");
        h.ConnectionName.Should().Be("Isolated");
    }

    [Fact]
    public void An_Include_Containing_Statements_Ends_The_Header_Like_A_Paste_Would()
    {
        // The fixture's own leading annotation is in the pasted header region (counts); the host
        // annotation AFTER the include is behind the fixture's first statement (does not count).
        Write("fixture.sql", "-- @setup FromFixture\ninsert into t values (1);");
        var main = Write("main.test.sql", "\\ir fixture.sql\n-- @setup TooLate\nselect 1;");

        var h = TestFileHeader.Parse(File.ReadAllText(main), main);

        h.Setup.Should().Equal("FromFixture");
    }
}
