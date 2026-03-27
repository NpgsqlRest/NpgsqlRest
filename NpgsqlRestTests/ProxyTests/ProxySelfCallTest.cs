namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ProxySelfCallTest()
    {
        script.Append(@"
        -- Proxy passthrough to self (relative path)
        create function proxy_self_passthrough()
        returns void
        language plpgsql
        as $$
        begin
            raise exception 'This should not be called';
        end;
        $$;
        comment on function proxy_self_passthrough() is 'HTTP GET
        proxy POST /api/hello-world';

        -- Proxy with response parameters to self (relative path)
        create function proxy_self_with_response(
            _proxy_status_code int = null,
            _proxy_body text = null,
            _proxy_success bool = null
        )
        returns json
        language plpgsql
        as $$
        begin
            return json_build_object(
                'status_code', _proxy_status_code,
                'body', _proxy_body,
                'success', _proxy_success
            );
        end;
        $$;
        comment on function proxy_self_with_response(int, text, bool) is 'HTTP GET
        proxy POST /api/hello-world';
");
    }
}

[Collection("TestFixture")]
public class ProxySelfCallTest(TestFixture test)
{
    [Fact]
    public async Task Proxy_SelfCall_Passthrough_ReturnsResponse()
    {
        using var response = await test.Client.GetAsync("/api/proxy-self-passthrough");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().Be("Hello World");
    }

    [Fact]
    public async Task Proxy_SelfCall_WithResponseParams_ReturnsStatusAndBody()
    {
        using var response = await test.Client.GetAsync("/api/proxy-self-with-response");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().Contain("\"status_code\" : 200");
        content.Should().Contain("\"body\" : \"Hello World\"");
        content.Should().Contain("\"success\" : true");
    }
}
