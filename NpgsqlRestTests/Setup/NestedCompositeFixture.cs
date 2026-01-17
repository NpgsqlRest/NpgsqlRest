using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace NpgsqlRestTests.Setup;

[CollectionDefinition("NestedCompositeFixture")]
public class NestedCompositeFixtureCollection : ICollectionFixture<NestedCompositeFixture> { }

/// <summary>
/// Test fixture for testing nested composite type resolution.
/// Creates a separate web application with ResolveNestedCompositeTypes = true.
/// </summary>
public class NestedCompositeFixture : IDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _client;

    public HttpClient Client => _client;

    public NestedCompositeFixture()
    {
        // Ensure the database exists. The Create() method is designed to be
        // called from multiple fixtures - it drops and recreates the database.
        // When running all tests together, xUnit may call this concurrently
        // with other fixtures, but since all fixtures create the same database
        // with the same schema, eventual consistency is achieved.
        Database.Create();

        var connectionString = Database.CreateAdditional("nested_composite_test");

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0"); // Use random available port

        _app = builder.Build();

        _app.UseNpgsqlRest(new NpgsqlRestOptions(connectionString)
        {
            IncludeSchemas = ["public"],
            CommentsMode = CommentsMode.ParseAll,
            RoutineSources = [new RoutineSource(resolveNestedCompositeTypes: true)]
        });

        _app.StartAsync().GetAwaiter().GetResult();

        var serverAddress = _app.Urls.First();
        _client = new HttpClient { BaseAddress = new Uri(serverAddress) };
        _client.Timeout = TimeSpan.FromHours(1);
    }

#pragma warning disable CA1816 // Dispose methods should call SuppressFinalize
    public void Dispose()
#pragma warning restore CA1816 // Dispose methods should call SuppressFinalize
    {
        _client.Dispose();
        _app.StopAsync().GetAwaiter().GetResult();
        _app.DisposeAsync().GetAwaiter().GetResult();
    }
}
