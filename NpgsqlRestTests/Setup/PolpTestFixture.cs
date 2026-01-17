using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace NpgsqlRestTests.Setup;

[CollectionDefinition("PolpTestFixture")]
public class PolpTestFixtureCollection : ICollectionFixture<PolpTestFixture> { }

/// <summary>
/// Test fixture for PoLP (Principle of Least Privilege) tests.
/// Creates a separate web application that uses test_user credentials
/// for BOTH metadata discovery AND function execution.
/// </summary>
public class PolpTestFixture : IDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _client;

    public HttpClient Client => _client;

    public PolpTestFixture()
    {
        // Ensure the database is created with superuser (postgres)
        // This creates all the schemas, types, functions, and grants needed for testing
        Database.Create();

        var connectionString = Database.CreatePolpConnection(); // Uses test_user

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0"); // Use random available port

        _app = builder.Build();

        _app.UseNpgsqlRest(new NpgsqlRestOptions(connectionString)
        {
            // Only include polp_schema - test_user only has USAGE on this schema
            IncludeSchemas = ["polp_schema"],
            CommentsMode = CommentsMode.ParseAll,
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
