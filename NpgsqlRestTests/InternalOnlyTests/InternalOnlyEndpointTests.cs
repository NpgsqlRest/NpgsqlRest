namespace NpgsqlRestTests;

public static partial class Database
{
    public static void InternalOnlyEndpointTests()
    {
        script.Append(@"
        -- Internal-only endpoint: returns data but NOT exposed as HTTP route
        create function internal_helper()
        returns text
        language sql
        as $$
        select 'internal data'
        $$;
        comment on function internal_helper() is 'HTTP GET
        internal';

        -- Internal-only with parameters
        create function internal_helper_with_param(_name text)
        returns text
        language sql
        as $$
        select 'Hello ' || _name
        $$;
        comment on function internal_helper_with_param(text) is 'HTTP POST
        internal';

        -- Proxy passthrough that calls the internal endpoint
        create function call_internal_helper()
        returns void
        language plpgsql
        as $$
        begin
            raise exception 'This should not be called';
        end;
        $$;
        comment on function call_internal_helper() is 'HTTP GET
        proxy GET /api/internal-helper';

        -- Proxy with response params that calls the internal endpoint
        create function call_internal_with_response(
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
        comment on function call_internal_with_response(int, text, bool) is 'HTTP GET
        proxy GET /api/internal-helper';

        -- internal_only alias test
        create function internal_alias_test()
        returns text
        language sql
        as $$
        select 'alias works'
        $$;
        comment on function internal_alias_test() is 'HTTP GET
        internal_only';
");
    }
}

[Collection("TestFixture")]
public class InternalOnlyEndpointTests(TestFixture test)
{
    [Fact]
    public async Task InternalOnly_DirectHttpCall_Returns404()
    {
        using var response = await test.Client.GetAsync("/api/internal-helper/");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task InternalOnly_WithParams_DirectHttpCall_Returns404()
    {
        using var content = new StringContent("{\"name\": \"World\"}", Encoding.UTF8, "application/json");
        using var response = await test.Client.PostAsync("/api/internal-helper-with-param/", content);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task InternalOnly_CalledViaProxy_ReturnsData()
    {
        using var response = await test.Client.GetAsync("/api/call-internal-helper/");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().Be("internal data");
    }

    [Fact]
    public async Task InternalOnly_CalledViaProxyWithResponseParams_ReturnsData()
    {
        using var response = await test.Client.GetAsync("/api/call-internal-with-response/");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().Contain("\"status_code\" : 200");
        content.Should().Contain("\"body\" : \"internal data\"");
        content.Should().Contain("\"success\" : true");
    }

    [Fact]
    public async Task InternalOnly_AliasAnnotation_Returns404()
    {
        using var response = await test.Client.GetAsync("/api/internal-alias-test/");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
