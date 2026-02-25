using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace NpgsqlRestTests;

// No separate Database method needed — reuses get_http_resolved_basic from ResolvedParameterBasicTests

[Collection("TestFixture")]
public class ResolvedParameterNullTests : IClassFixture<WireMockFixture>
{
    private readonly TestFixture _test;
    private readonly WireMockServer _server;

    public ResolvedParameterNullTests(TestFixture test, WireMockFixture wireMock)
    {
        _test = test;
        _server = wireMock.Server;
        _server.Reset();
    }

    [Fact]
    public async Task Test_resolved_param_null_when_sql_returns_no_rows()
    {
        // Name "nonexistent_user" doesn't exist in http_api_tokens table.
        // So _token resolves to NULL → placeholder becomes empty string.
        // The HTTP request is still made (with empty/missing auth value in header).
        // get_http_resolved_basic has annotation: _token = select api_token ... where user_name = {_name}
        _server
            .Given(Request.Create()
                .WithPath("/api/resolved/protected")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("{\"data\": \"no-auth-data\"}"));

        using var response = await _test.Client.GetAsync("/api/get-http-resolved-basic/?name=nonexistent_user");
        var content = await response.Content.ReadAsStringAsync();

        // The request should still succeed — the PG function receives NULL for _token
        response?.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
