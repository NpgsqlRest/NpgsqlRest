namespace NpgsqlRestTests;

public static partial class Database
{
    public static void HttpClientTypeSelfCallTests()
    {
        script.Append(@"
        -- HTTP type with relative path (self-call to same server)
        create type http_self_call as (
            body text,
            status_code int,
            success bool
        );
        comment on type http_self_call is 'POST /api/hello-world';

        create function get_http_self_call(
            req http_self_call
        )
        returns text
        language plpgsql
        as
        $$
        begin
            return (req).body;
        end;
        $$;

        create function get_http_self_call_status(
            req http_self_call
        )
        returns int
        language plpgsql
        as
        $$
        begin
            return (req).status_code;
        end;
        $$;

        create function get_http_self_call_success(
            req http_self_call
        )
        returns bool
        language plpgsql
        as
        $$
        begin
            return (req).success;
        end;
        $$;
");
    }
}

[Collection("TestFixture")]
public class HttpClientTypeSelfCallTests(TestFixture test)
{
    [Fact]
    public async Task SelfCall_RelativePath_ReturnsBody()
    {
        // /api/hello-world returns 'Hello World' — the HTTP type calls it internally
        using var response = await test.Client.GetAsync("/api/get-http-self-call");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().Be("Hello World");
    }

    [Fact]
    public async Task SelfCall_RelativePath_ReturnsStatusCode200()
    {
        using var response = await test.Client.GetAsync("/api/get-http-self-call-status");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().Be("200");
    }

    [Fact]
    public async Task SelfCall_RelativePath_ReturnsSuccessTrue()
    {
        using var response = await test.Client.GetAsync("/api/get-http-self-call-success");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().Be("t");
    }
}
