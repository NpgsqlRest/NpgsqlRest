using WireMock.Server;
using WireMock.Settings;

namespace NpgsqlRestTests;

public class ProxyWireMockFixture : IDisposable
{
    public const int Port = 50954;
    public WireMockServer Server { get; }

    public ProxyWireMockFixture() => Server = WireMockServer.Start(new WireMockServerSettings { Port = Port });
    public void Dispose() => Server.Stop();
}
