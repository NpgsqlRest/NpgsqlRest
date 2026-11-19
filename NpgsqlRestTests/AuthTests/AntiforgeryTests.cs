using System.Net;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void AntiforgeryTests()
    {
        script.Append("""
        create function af_post(_value text) returns text language sql as $$ select 'got ' || _value $$;
        comment on function af_post(text) is 'HTTP POST';

        create function af_get() returns text language sql stable as $$ select 'read' $$;
        comment on function af_get() is 'HTTP GET';
        """);
    }
}

/// <summary>
/// Security regression: with antiforgery enabled, state-changing NpgsqlRest endpoints must require a valid
/// antiforgery token. They used to accept every request - the middleware skipped them (no antiforgery metadata),
/// and even with metadata it only records a failed validation instead of rejecting the request.
/// </summary>
[Collection("AntiforgeryTestFixture")]
public class AntiforgeryTests(AntiforgeryTestFixture test)
{
    private HttpClient NewClient(out CookieContainer cookies)
    {
        cookies = new CookieContainer();
        return new HttpClient(new HttpClientHandler { CookieContainer = cookies, UseCookies = true })
        {
            BaseAddress = new Uri(test.ServerAddress)
        };
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    [Fact]
    public async Task Post_without_token_is_rejected()
    {
        using var client = NewClient(out _);
        using var response = await client.PostAsync("/api/af-post", Json("{\"value\":\"x\"}"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Be(
            "{\"type\":\"https://tools.ietf.org/html/rfc9110#section-15.5.1\",\"title\":\"Bad Request\",\"status\":400,\"detail\":\"Invalid or missing antiforgery token.\"}");
    }

    [Fact]
    public async Task Post_with_wrong_token_is_rejected()
    {
        using var client = NewClient(out _);
        (await client.GetStringAsync("/af-token")).Should().NotBeNullOrEmpty(); // antiforgery cookie issued

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/af-post") { Content = Json("{\"value\":\"x\"}") };
        request.Headers.Add(AntiforgeryTestFixture.HeaderName, "forged-token");
        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_with_valid_token_succeeds()
    {
        using var client = NewClient(out _);
        var token = await client.GetStringAsync("/af-token");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/af-post") { Content = Json("{\"value\":\"x\"}") };
        request.Headers.Add(AntiforgeryTestFixture.HeaderName, token);
        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("got x");
    }

    [Fact]
    public async Task Get_does_not_require_a_token()
    {
        using var client = NewClient(out _);
        using var response = await client.GetAsync("/api/af-get");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("read");
    }
}
