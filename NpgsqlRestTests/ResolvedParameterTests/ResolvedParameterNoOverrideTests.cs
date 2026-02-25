using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace NpgsqlRestTests;

// No separate Database method needed — reuses get_http_resolved_basic from ResolvedParameterBasicTests

[Collection("TestFixture")]
public class ResolvedParameterNoOverrideTests : IClassFixture<WireMockFixture>
{
    private readonly TestFixture _test;
    private readonly WireMockServer _server;

    public ResolvedParameterNoOverrideTests(TestFixture test, WireMockFixture wireMock)
    {
        _test = test;
        _server = wireMock.Server;
        _server.Reset();
    }

    [Fact]
    public async Task Test_resolved_param_cannot_be_overridden_by_client()
    {
        // Client sends token=hacked-token, but the resolved value from DB should be used.
        // WireMock expects the DB-resolved token, NOT the client-supplied one.
        // get_http_resolved_basic has annotation: _token = select api_token ... where user_name = {_name}
        _server
            .Given(Request.Create()
                .WithPath("/api/resolved/protected")
                .WithHeader("Authorization", "Bearer secret-token-abc123")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("{\"data\": \"secure-data\"}"));

        // Explicitly try to override _token via query string
        using var response = await _test.Client.GetAsync("/api/get-http-resolved-basic/?name=myname&token=hacked-token");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("secure-data");
    }
}
