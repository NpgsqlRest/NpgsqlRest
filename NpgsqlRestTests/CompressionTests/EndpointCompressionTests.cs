using System.IO.Compression;
using System.Net.Http.Headers;
using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void EndpointCompressionTests()
        {
            script.Append("""
create function compress_rows_get()
returns table (id int, name text)
language sql as $$ select i, 'name ' || i from generate_series(1, 200) i $$;
comment on function compress_rows_get() is 'HTTP GET';
""");
        }
    }
}

namespace NpgsqlRestTests.CompressionTests
{
    /// <summary>
    /// NpgsqlRest endpoints write to Response.BodyWriter; with the response compression middleware (which replaces the
    /// response body) the output must still be compressed and complete.
    /// </summary>
    [Collection("CompressionTestFixture")]
    public class EndpointCompressionTests(CompressionTestFixture test)
    {
        private static readonly string ExpectedBody =
            "[" + string.Join(",", Enumerable.Range(1, 200).Select(i => $"{{\"id\":{i},\"name\":\"name {i}\"}}")) + "]";

        [Theory]
        [InlineData("br")]
        [InlineData("gzip")]
        public async Task Endpoint_response_is_compressed_and_complete(string encoding)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/compress-rows-get");
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue(encoding));
            using var response = await test.Client.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Content.Headers.ContentEncoding.Should().Equal(encoding);

            await using var compressed = await response.Content.ReadAsStreamAsync();
            await using Stream decompressor = encoding == "br"
                ? new BrotliStream(compressed, CompressionMode.Decompress)
                : new GZipStream(compressed, CompressionMode.Decompress);
            using var reader = new StreamReader(decompressor);
            (await reader.ReadToEndAsync()).Should().Be(ExpectedBody);
        }

        [Fact]
        public async Task Endpoint_response_without_accept_encoding_is_plain()
        {
            using var response = await test.Client.GetAsync("/api/compress-rows-get");
            response.Content.Headers.ContentEncoding.Should().BeEmpty();
            (await response.Content.ReadAsStringAsync()).Should().Be(ExpectedBody);
        }
    }
}
