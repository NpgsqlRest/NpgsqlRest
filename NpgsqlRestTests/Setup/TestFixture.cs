namespace NpgsqlRestTests.Setup;

[CollectionDefinition("TestFixture")]
public class TestFixtureCollection : ICollectionFixture<TestFixture> { }

public class TestFixture : IDisposable
{
    private readonly WebApplicationFactory<Program> _application;
    private readonly HttpClient _client;

    public HttpClient Client => _client;
    public WebApplicationFactory<Program> Application => _application;

    public TestFixture()
    {
        _application = new WebApplicationFactory<Program>();
        _client = _application.CreateClient();
        _client.Timeout = TimeSpan.FromHours(1);

        // Enable self-referencing calls via TestServer's in-memory handler
        var selfClient = _application.CreateClient();
        selfClient.Timeout = TimeSpan.FromHours(1);
        NpgsqlRest.HttpClientType.HttpClientTypeHandler.SetSelfClient(selfClient);
        NpgsqlRest.Proxy.ProxyRequestHandler.SetSelfClient(selfClient);
    }

#pragma warning disable CA1816 // Dispose methods should call SuppressFinalize
    public void Dispose()
#pragma warning restore CA1816 // Dispose methods should call SuppressFinalize
    {
        _client.Dispose();
        _application.Dispose();
    }
}