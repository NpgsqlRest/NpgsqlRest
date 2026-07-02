using NpgsqlRestClient.Testing;

namespace NpgsqlRestTests.TestRunnerTests.ParserTests;

public class HttpFileRequestParserTests
{
    private static HttpStep Parse(string body)
    {
        var step = HttpFileRequestParser.TryParse(body, 1);
        step.Should().NotBeNull("the block should be recognized as an HTTP request");
        return step!;
    }

    [Fact]
    public void Get_With_Path_Is_Recognized()
    {
        var s = Parse("GET /get-users");
        s.Method.Should().Be("GET");
        s.Path.Should().Be("/get-users");
        s.Body.Should().BeNull();
    }

    [Fact]
    public void Query_String_Is_Kept_In_Path()
    {
        Parse("GET /users?active=true&role=admin").Path.Should().Be("/users?active=true&role=admin");
    }

    [Fact]
    public void Leading_Http_Keyword_Is_Tolerated()
    {
        var s = Parse("HTTP POST /things");
        s.Method.Should().Be("POST");
        s.Path.Should().Be("/things");
    }

    [Fact]
    public void Method_Is_Case_Insensitive_And_Uppercased()
    {
        Parse("post /things").Method.Should().Be("POST");
    }

    [Fact]
    public void Trailing_Http_Version_Is_Ignored()
    {
        var s = Parse("GET /users HTTP/1.1");
        s.Method.Should().Be("GET");
        s.Path.Should().Be("/users");
    }

    [Theory]
    [InlineData("this is just a note")]
    [InlineData("/api/foo")]                 // no method
    [InlineData("GET the data")]             // path doesn't start with '/'
    [InlineData("PATCH /things")]            // method not in GET|PUT|POST|DELETE
    [InlineData("HEAD /things")]
    [InlineData("GET /data from the cache")] // trailing junk that isn't a version
    [InlineData("")]
    public void Non_Request_Blocks_Are_Not_Recognized(string body)
    {
        HttpFileRequestParser.TryParse(body, 1).Should().BeNull();
    }

    [Fact]
    public void Headers_Are_Parsed_Until_Blank_Line()
    {
        var s = Parse("POST /things\nContent-Type: application/json\nX-Trace: abc\n\n{\"a\":1}");
        s.Headers.Should().HaveCount(2);
        s.Headers.Should().Contain(("Content-Type", "application/json"));
        s.Headers.Should().Contain(("X-Trace", "abc"));
        s.Body.Should().Be("{\"a\":1}");
    }

    [Fact]
    public void Claim_Directive_Adds_Claim()
    {
        var s = Parse("POST /things\n# @claim user_id=123\n\n{}");
        s.Claims.Should().ContainSingle()
            .Which.Should().Be(("user_id", "123"));
    }

    [Fact]
    public void Repeated_Claim_Type_Yields_Multiple_Values()
    {
        var s = Parse("POST /things\n# @claim roles=admin\n# @claim roles=editor\n\n{}");
        s.Claims.Should().HaveCount(2);
        s.Claims.Should().Contain(("roles", "admin"));
        s.Claims.Should().Contain(("roles", "editor"));
    }

    [Fact]
    public void Response_Directive_Is_Parsed_And_Unknown_Directives_Are_Ignored()
    {
        // `@expect-status` was removed (assert on the response temp table's status column instead);
        // an unknown directive is silently ignored rather than breaking the block.
        var s = Parse("GET /x\n# @expect-status 404\n# @response listing");
        s.ResponseTable.Should().Be("listing");
    }

    [Fact]
    public void Double_Slash_Comment_And_Directive_Variants_Work()
    {
        var s = Parse("// a note\nGET /x\n// @claim user_id=7");
        s.Method.Should().Be("GET");
        s.Claims.Should().ContainSingle().Which.Value.Should().Be("7");
    }

    [Fact]
    public void Leading_Comment_Lines_Before_Request_Are_Skipped()
    {
        var s = Parse("# create the thing\nPOST /things\n\n{\"name\":\"x\"}");
        s.Method.Should().Be("POST");
        s.Body.Should().Be("{\"name\":\"x\"}");
    }

    [Fact]
    public void Body_With_Colons_Is_Not_Parsed_As_Headers()
    {
        var s = Parse("POST /things\n\n{\"url\": \"http://x\", \"k\": \"v\"}");
        s.Headers.Should().BeEmpty();
        s.Body.Should().Be("{\"url\": \"http://x\", \"k\": \"v\"}");
    }

    [Fact]
    public void Multiline_Body_Is_Preserved()
    {
        var s = Parse("POST /things\n\n{\n  \"a\": 1\n}");
        s.Body.Should().Be("{\n  \"a\": 1\n}");
    }
}
