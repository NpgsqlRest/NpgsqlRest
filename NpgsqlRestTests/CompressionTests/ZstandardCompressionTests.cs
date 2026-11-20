using System.IO.Compression;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NpgsqlRestClient;

namespace NpgsqlRestTests.CompressionTests;

/// <summary>
/// Response compression as the client sets it up (ResponseCompressionSetup, the ResponseCompression section),
/// with Zstandard (ResponseCompression:UseZstandard, .NET 11).
/// </summary>
public class ZstandardCompressionTests
{
    // long enough and repetitive enough that every algorithm makes it smaller
    private static readonly string Json =
        "[" + string.Join(",", Enumerable.Range(1, 400).Select(i => $"{{\"id\":{i},\"name\":\"customer number {i}\",\"active\":true}}")) + "]";

    private sealed class Server : IAsyncDisposable
    {
        public required WebApplication App { get; init; }
        public required HttpClient Client { get; init; }
        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.StopAsync();
            await App.DisposeAsync();
        }
    }

    private static async Task<Server> StartAsync(
        bool zstandard, bool brotli = true, bool gzip = true,
        CompressionLevel level = CompressionLevel.Optimal, int? quality = null, string[]? excluded = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var added = ResponseCompressionSetup.Add(builder.Services, enableForHttps: false,
            useZstandard: zstandard, useBrotli: brotli, useGzip: gzip, level: level,
            zstandardQuality: quality, excludedMimeTypes: excluded);
        var app = builder.Build();
        if (added)
        {
            app.UseResponseCompression();
        }
        app.MapGet("/data", () => Results.Text(Json, "application/json"));
        app.MapGet("/image", () => Results.Text(Json, "image/png"));
        await app.StartAsync();
        var client = new HttpClient(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.None })
        {
            BaseAddress = new Uri(app.Urls.First())
        };
        return new Server { App = app, Client = client };
    }

    private static async Task<(string? encoding, byte[] body, string? vary)> GetAsync(Server server, string? acceptEncoding, string path = "/data")
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (acceptEncoding is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept-Encoding", acceptEncoding);
        }
        using var response = await server.Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var vary = response.Headers.TryGetValues("Vary", out var v) ? string.Join(",", v) : null;
        return (response.Content.Headers.ContentEncoding.SingleOrDefault(), await response.Content.ReadAsByteArrayAsync(), vary);
    }

    private static string Decode(byte[] body, string? encoding)
    {
        using var source = new MemoryStream(body);
        using Stream stream = encoding switch
        {
#if NET11_0_OR_GREATER
            "zstd" => new ZstandardStream(source, CompressionMode.Decompress),
#endif
            "br" => new BrotliStream(source, CompressionMode.Decompress),
            "gzip" => new GZipStream(source, CompressionMode.Decompress),
            null => source,
            _ => throw new InvalidOperationException($"unexpected encoding {encoding}")
        };
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    [Theory]
    [InlineData(CompressionLevel.Optimal, 3)]
    [InlineData(CompressionLevel.Fastest, 1)]
    [InlineData(CompressionLevel.SmallestSize, 19)]
    [InlineData(CompressionLevel.NoCompression, 1)]
    public void Compression_level_stands_for_a_zstandard_quality(CompressionLevel level, int quality)
    {
        ResponseCompressionSetup.ZstandardQualityFor(level).Should().Be(quality);
    }

    [Fact]
    public void Nothing_is_registered_when_every_algorithm_is_off()
    {
        var services = new ServiceCollection();
        ResponseCompressionSetup.Add(services, false, useZstandard: false, useBrotli: false, useGzip: false, CompressionLevel.Optimal)
            .Should().BeFalse();
        services.Should().BeEmpty();
    }

    [Fact]
    public async Task Without_accept_encoding_the_response_is_not_compressed()
    {
        await using var server = await StartAsync(zstandard: true);
        var (encoding, body, vary) = await GetAsync(server, null);
        encoding.Should().BeNull();
        Encoding.UTF8.GetString(body).Should().Be(Json);
#if NET11_0_OR_GREATER
        // ASP.NET Core 11: Vary is set on every response the middleware sees, compressed or not
        vary.Should().Be("Accept-Encoding");
#else
        vary.Should().BeNull();
#endif
    }

    [Theory]
    [InlineData("br", "br")]
    [InlineData("gzip", "gzip")]
    [InlineData("gzip, br", "br")]
    [InlineData("br, gzip", "br")]
    public async Task Brotli_and_gzip_are_chosen_as_before_when_zstandard_is_on(string acceptEncoding, string expected)
    {
        await using var server = await StartAsync(zstandard: true);
        var (encoding, body, _) = await GetAsync(server, acceptEncoding);
        encoding.Should().Be(expected);
        Decode(body, encoding).Should().Be(Json);
    }

    [Theory]
    [InlineData("zstd, br, gzip", "br")]
    [InlineData("gzip, zstd", "gzip")]
    public async Task Zstandard_is_not_used_unless_it_is_turned_on(string acceptEncoding, string expected)
    {
        await using var server = await StartAsync(zstandard: false);
        var (encoding, body, _) = await GetAsync(server, acceptEncoding);
        encoding.Should().Be(expected);
        Decode(body, encoding).Should().Be(Json);
    }

    [Fact]
    public async Task A_client_that_accepts_only_zstandard_gets_no_compression_when_it_is_off()
    {
        await using var server = await StartAsync(zstandard: false);
        var (encoding, body, _) = await GetAsync(server, "zstd");
        encoding.Should().BeNull();
        Encoding.UTF8.GetString(body).Should().Be(Json);
    }

    [Fact]
    public async Task A_type_that_is_not_listed_is_not_compressed()
    {
        await using var server = await StartAsync(zstandard: true);
        var (encoding, body, _) = await GetAsync(server, "zstd, br, gzip", "/image");
        encoding.Should().BeNull();
        Encoding.UTF8.GetString(body).Should().Be(Json);
    }

    [Fact]
    public async Task An_excluded_type_is_not_compressed()
    {
        await using var server = await StartAsync(zstandard: true, excluded: ["application/json"]);
        var (encoding, body, _) = await GetAsync(server, "zstd, br, gzip");
        encoding.Should().BeNull();
        Encoding.UTF8.GetString(body).Should().Be(Json);
    }

#if NET11_0_OR_GREATER
    [Fact]
    public void This_build_supports_zstandard()
    {
        ResponseCompressionSetup.ZstandardSupported.Should().BeTrue();
    }

    [Theory]
    [InlineData("zstd")]
    [InlineData("zstd, br, gzip")]
    [InlineData("gzip, br, zstd")]
    [InlineData("gzip, deflate, br, zstd")] // what browsers send
    [InlineData("br;q=0.9, zstd;q=0.9, gzip;q=0.5")]
    [InlineData("ZSTD")]
    public async Task Zstandard_is_chosen_when_the_client_accepts_it(string acceptEncoding)
    {
        await using var server = await StartAsync(zstandard: true);
        var (encoding, body, vary) = await GetAsync(server, acceptEncoding);
        encoding.Should().Be("zstd");
        vary.Should().Be("Accept-Encoding");
        body.Length.Should().BeLessThan(Encoding.UTF8.GetByteCount(Json) / 4);
        Decode(body, encoding).Should().Be(Json);
    }

    [Theory]
    [InlineData("zstd;q=0.5, br;q=1.0", "br")]
    [InlineData("zstd;q=0.1, gzip", "gzip")]
    [InlineData("zstd;q=0, br", "br")]
    [InlineData("zstd;q=0, gzip", "gzip")]
    public async Task The_clients_preference_wins_over_the_order_of_algorithms(string acceptEncoding, string expected)
    {
        await using var server = await StartAsync(zstandard: true);
        var (encoding, body, _) = await GetAsync(server, acceptEncoding);
        encoding.Should().Be(expected);
        Decode(body, encoding).Should().Be(Json);
    }

    [Fact]
    public async Task Zstandard_works_as_the_only_algorithm()
    {
        await using var server = await StartAsync(zstandard: true, brotli: false, gzip: false);
        var (encoding, body, _) = await GetAsync(server, "gzip, br, zstd");
        encoding.Should().Be("zstd");
        Decode(body, encoding).Should().Be(Json);

        var (none, plain, _) = await GetAsync(server, "gzip, br");
        none.Should().BeNull();
        Encoding.UTF8.GetString(plain).Should().Be(Json);
    }

    [Theory]
    [InlineData(CompressionLevel.Fastest)]
    [InlineData(CompressionLevel.Optimal)]
    [InlineData(CompressionLevel.SmallestSize)]
    [InlineData(CompressionLevel.NoCompression)]
    public async Task Every_compression_level_gives_a_response_that_decodes(CompressionLevel level)
    {
        await using var server = await StartAsync(zstandard: true, level: level);
        var (encoding, body, _) = await GetAsync(server, "zstd");
        encoding.Should().Be("zstd");
        Decode(body, encoding).Should().Be(Json);
        server.App.Services.GetRequiredService<IOptions<ZstandardCompressionProviderOptions>>().Value
            .CompressionOptions.Quality.Should().Be(ResponseCompressionSetup.ZstandardQualityFor(level));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    [InlineData(22)]
    public async Task An_explicit_quality_overrides_the_compression_level(int quality)
    {
        await using var server = await StartAsync(zstandard: true, level: CompressionLevel.Fastest, quality: quality);
        server.App.Services.GetRequiredService<IOptions<ZstandardCompressionProviderOptions>>().Value
            .CompressionOptions.Quality.Should().Be(quality);
        var (encoding, body, _) = await GetAsync(server, "zstd");
        encoding.Should().Be("zstd");
        Decode(body, encoding).Should().Be(Json);
    }

    [Fact]
    public async Task A_higher_quality_gives_a_smaller_response()
    {
        await using var fast = await StartAsync(zstandard: true, quality: 1);
        await using var small = await StartAsync(zstandard: true, quality: 19);
        var (_, fastBody, _) = await GetAsync(fast, "zstd");
        var (_, smallBody, _) = await GetAsync(small, "zstd");
        smallBody.Length.Should().BeLessThan(fastBody.Length);
    }
#else
    [Fact]
    public async Task On_net10_zstandard_is_not_available_and_the_other_algorithms_are_used()
    {
        ResponseCompressionSetup.ZstandardSupported.Should().BeFalse();
        await using var server = await StartAsync(zstandard: true);
        var (encoding, body, _) = await GetAsync(server, "zstd, br, gzip");
        encoding.Should().Be("br");
        Decode(body, encoding).Should().Be(Json);
    }
#endif
}
