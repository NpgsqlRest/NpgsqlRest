using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ServiceProviderModeRequestTests()
    {
        script.Append("""
create function spmode_get() returns text language sql as 'select ''from service provider''';
comment on function spmode_get() is 'HTTP GET';
""");
    }
}

/// <summary>
/// Requests in ServiceProviderMode take the data source / connection from the request's services. (The endpoint
/// handler no longer receives an IServiceProvider argument - binding it created a DI scope for every request - and
/// resolves context.RequestServices only in this mode.)
/// </summary>
public class ServiceProviderModeRequestTests
{
    private static async Task<string> RunAsync(ServiceProviderObject mode, Action<IServiceCollection, string> register)
    {
        var connectionString = Database.Create();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        register(builder.Services, connectionString);
        await using var app = builder.Build();
        app.UseNpgsqlRest(new NpgsqlRestOptions
        {
            ServiceProviderMode = mode,
            IncludeSchemas = ["public"],
            NameSimilarTo = "spmode[_]%",
            RequiresAuthorization = false,
        });
        await app.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
            using var response = await client.GetAsync("/api/spmode-get");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            return await response.Content.ReadAsStringAsync();
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task DataSource_from_services_serves_requests()
    {
        var body = await RunAsync(ServiceProviderObject.NpgsqlDataSource,
            (services, cs) => services.AddSingleton(NpgsqlDataSource.Create(cs)));
        body.Should().Be("from service provider");
    }

    [Fact]
    public async Task Scoped_connection_from_services_serves_requests()
    {
        var body = await RunAsync(ServiceProviderObject.NpgsqlConnection,
            (services, cs) => services.AddScoped(_ => new NpgsqlConnection(cs)));
        body.Should().Be("from service provider");
    }
}
