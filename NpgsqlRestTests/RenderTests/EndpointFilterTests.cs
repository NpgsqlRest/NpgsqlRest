using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void EndpointFilterTests()
    {
        script.Append("""
        create function epf_filtered(_value text) returns text language sql stable as $$ select 'filtered ' || _value $$;
        comment on function epf_filtered(text) is 'HTTP GET';

        create function epf_rows() returns table (id int, name text) language sql stable as $$
        select i, 'name ' || i from generate_series(1, 3) i $$;
        comment on function epf_rows() is 'HTTP GET';

        create function epf_plain() returns text language sql stable as $$ select 'plain' $$;
        comment on function epf_plain() is 'HTTP GET';
        """);
    }
}

[CollectionDefinition("EndpointFilterTestFixture")]
public class EndpointFilterTestFixtureCollection : ICollectionFixture<EndpointFilterTestFixture> { }

/// <summary>
/// NpgsqlRest endpoints with an endpoint filter added through RouteHandlerCreated (every endpoint except epf_plain).
/// The filter marks the response with a header and rejects requests that send ?deny.
/// </summary>
public class EndpointFilterTestFixture : IDisposable
{
    private readonly WebApplication _app;

    public string ServerAddress { get; }

    public EndpointFilterTestFixture()
    {
        var connectionString = Database.Create();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        _app = builder.Build();
        _app.UseNpgsqlRest(new(connectionString)
        {
            IncludeSchemas = ["public"],
            NameSimilarTo = "epf[_]%",
            CommentsMode = CommentsMode.ParseAll,
            RequiresAuthorization = false,
            RouteHandlerCreated = (routeBuilder, endpoint) =>
            {
                if (endpoint.Path.EndsWith("/epf-plain", StringComparison.Ordinal))
                {
                    return;
                }
                routeBuilder.AddEndpointFilter(async (invocationContext, next) =>
                {
                    var http = invocationContext.HttpContext;
                    if (http.Request.Query.ContainsKey("deny"))
                    {
                        return Results.Text("denied by filter", statusCode: StatusCodes.Status403Forbidden);
                    }
                    http.Response.Headers["X-Endpoint-Filter"] = "ran";
                    return await next(invocationContext);
                });
            },
        });

        _app.StartAsync().GetAwaiter().GetResult();
        ServerAddress = _app.Urls.First();
    }

#pragma warning disable CA1816
    public void Dispose()
#pragma warning restore CA1816
    {
        _app.StopAsync().GetAwaiter().GetResult();
        _app.DisposeAsync().GetAwaiter().GetResult();
    }
}

/// <summary>
/// An endpoint without endpoint filters calls the NpgsqlRest handler directly, skipping the generated route handler.
/// Endpoint filters (added through RouteHandlerCreated) must keep working: they run in that generated handler.
/// </summary>
[Collection("EndpointFilterTestFixture")]
public class EndpointFilterTests(EndpointFilterTestFixture test)
{
    private HttpClient NewClient() => new() { BaseAddress = new Uri(test.ServerAddress) };

    [Fact]
    public async Task Filter_runs_before_a_scalar_endpoint()
    {
        using var client = NewClient();
        using var response = await client.GetAsync("/api/epf-filtered/?value=x");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Endpoint-Filter").Should().Equal("ran");
        (await response.Content.ReadAsStringAsync()).Should().Be("filtered x");
    }

    [Fact]
    public async Task Filter_runs_before_a_rows_endpoint()
    {
        using var client = NewClient();
        using var response = await client.GetAsync("/api/epf-rows/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Endpoint-Filter").Should().Equal("ran");
        (await response.Content.ReadAsStringAsync()).Should().Be("[{\"id\":1,\"name\":\"name 1\"},{\"id\":2,\"name\":\"name 2\"},{\"id\":3,\"name\":\"name 3\"}]");
    }

    [Fact]
    public async Task Filter_can_reject_the_request_and_the_routine_does_not_run()
    {
        using var client = NewClient();
        using var response = await client.GetAsync("/api/epf-filtered/?value=x&deny=1");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Be("denied by filter");
    }

    [Fact]
    public async Task Endpoint_without_a_filter_is_served_directly()
    {
        using var client = NewClient();
        using var response = await client.GetAsync("/api/epf-plain/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains("X-Endpoint-Filter").Should().BeFalse();
        (await response.Content.ReadAsStringAsync()).Should().Be("plain");
    }
}
