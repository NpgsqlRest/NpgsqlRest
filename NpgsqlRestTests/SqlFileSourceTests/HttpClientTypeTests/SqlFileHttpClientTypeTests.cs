using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace NpgsqlRestTests.SqlFileSourceTests;

[Collection("SqlFileHttpClientTypeFixture")]
public class SqlFileHttpClientTypeTests(SqlFileHttpClientTypeFixture test)
{
    [Fact]
    public async Task SqlFile_HttpType_ReturnsBody()
    {
        test.Server.Reset();
        test.Server
            .Given(Request.Create().WithPath("/api/sf-test1").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("hello from http"));

        using var response = await test.Client.GetAsync("/api/sf-http-body-test");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().Be("[\"hello from http\"]");
    }

    [Fact]
    public async Task SqlFile_HttpType_FullFields_ReturnsBodyStatusAndSuccess()
    {
        test.Server.Reset();
        test.Server
            .Given(Request.Create().WithPath("/api/sf-test2").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("full response"));

        using var response = await test.Client.GetAsync("/api/sf-http-full-test");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().Be("""[{"responseBody":"full response","status":200,"ok":true}]""");
    }
}
