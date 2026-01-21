using System.Text;
using System.Web;
using BenchmarkDotNet.Attributes;

namespace BenchmarkTests;

/// <summary>
/// HTTP endpoint benchmark tests for full-stack performance measurement.
///
/// IMPORTANT: These benchmarks require the NpgsqlRestTests server to be running.
/// Start the server with: dotnet run --project NpgsqlRestTests/Setup
///
/// Main endpoint tested: public.perf_test - returns all common PostgreSQL types
/// </summary>
[MemoryDiagnoser]
public class HttpClientTests
{
    private HttpClient _client = null!;

    // Base URL for the test server
    private const string BaseUrl = "http://localhost:5000";

    // Pre-built query strings for different test scenarios
    private string _perfTestUrl_10Rows = null!;
    private string _perfTestUrl_100Rows = null!;
    private string _perfTestUrl_1000Rows = null!;

    [GlobalSetup]
    public void Setup()
    {
        _client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        // Build query string for perf_test function
        // Parameters: records, text, int, bigint, numeric, real, double, bool, date, timestamp, timestamptz, uuid, json, jsonb, int_array, text_array
        _perfTestUrl_10Rows = BuildPerfTestUrl(10);
        _perfTestUrl_100Rows = BuildPerfTestUrl(100);
        _perfTestUrl_1000Rows = BuildPerfTestUrl(1000);
    }

    private static string BuildPerfTestUrl(int records)
    {
        var queryParams = new Dictionary<string, string>
        {
            ["records"] = records.ToString(),
            ["text"] = "BenchmarkText",
            ["int"] = "42",
            ["bigint"] = "9223372036854775000",
            ["numeric"] = "12345.6789",
            ["real"] = "3.14159",
            ["double"] = "2.718281828459045",
            ["bool"] = "true",
            ["date"] = "2024-01-15",
            ["timestamp"] = "2024-01-15T10:30:00",
            ["timestamptz"] = "2024-01-15T10:30:00Z",
            ["uuid"] = "550e8400-e29b-41d4-a716-446655440000",
            ["json"] = """{"key":"value","num":123}""",
            ["jsonb"] = """{"nested":{"data":true}}""",
            ["intArray"] = "{1,2,3,4,5}",
            ["textArray"] = "{\"a\",\"b\",\"c\"}"
        };

        var queryString = string.Join("&", queryParams.Select(p => $"{p.Key}={HttpUtility.UrlEncode(p.Value)}"));
        return $"{BaseUrl}/api/perf-test?{queryString}";
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _client.Dispose();
    }

    /// <summary>
    /// Benchmark: Full-type test with 10 rows
    /// Tests all PostgreSQL types: text, int, bigint, numeric, real, double, bool,
    /// date, time, timestamp, timestamptz, interval, uuid, json, jsonb, arrays, nullables
    /// </summary>
    [Benchmark]
    public async Task<string> PerfTest_AllTypes_10Rows()
    {
        using var result = await _client.GetAsync(_perfTestUrl_10Rows);
        return await result.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// Benchmark: Full-type test with 100 rows
    /// Tests serialization performance with moderate data volume
    /// </summary>
    [Benchmark]
    public async Task<string> PerfTest_AllTypes_100Rows()
    {
        using var result = await _client.GetAsync(_perfTestUrl_100Rows);
        return await result.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// Benchmark: Full-type test with 1000 rows
    /// Tests serialization performance at scale
    /// </summary>
    [Benchmark]
    public async Task<string> PerfTest_AllTypes_1000Rows()
    {
        using var result = await _client.GetAsync(_perfTestUrl_1000Rows);
        return await result.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// Benchmark: Simple table (int + text only) for comparison
    /// </summary>
    [Benchmark]
    public async Task<string> SimpleTable_100Rows()
    {
        using var result = await _client.GetAsync($"{BaseUrl}/api/case-get-long-table1/?records=100");
        return await result.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// Benchmark: Cached endpoint
    /// Tests cache hit scenario
    /// </summary>
    [Benchmark]
    public async Task<string> CachedSet_50Rows()
    {
        using var result = await _client.GetAsync($"{BaseUrl}/api/cache-get-set/?count=50");
        return await result.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// Benchmark: JSON record endpoint
    /// Tests JSON type serialization
    /// </summary>
    [Benchmark]
    public async Task<string> JsonRecord()
    {
        using var result = await _client.GetAsync($"{BaseUrl}/api/cache-get-json-record/?key=benchmark");
        return await result.Content.ReadAsStringAsync();
    }
}

/// <summary>
/// Parameterized HTTP benchmarks with varying row counts.
/// Uses perf_test function to measure serialization scaling across all PostgreSQL types.
/// </summary>
[MemoryDiagnoser]
public class HttpScalingBenchmarks
{
    private HttpClient _client = null!;
    private const string BaseUrl = "http://localhost:5000";

    private string[] _prebuiltUrls = null!;

    [Params(10, 50, 100, 250, 500, 1000)]
    public int RowCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _client = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };

        // Pre-build URLs for each row count
        _prebuiltUrls = new[] { 10, 50, 100, 250, 500, 1000 }
            .Select(BuildPerfTestUrl)
            .ToArray();
    }

    private static string BuildPerfTestUrl(int records)
    {
        return $"{BaseUrl}/api/perf-test?" +
               $"records={records}&" +
               $"text=ScalingTest&" +
               $"int=100&" +
               $"bigint=9223372036854775000&" +
               $"numeric=999.9999&" +
               $"real=1.5&" +
               $"double=2.5&" +
               $"bool=true&" +
               $"date=2024-06-15&" +
               $"timestamp=2024-06-15T12:00:00&" +
               $"timestamptz=2024-06-15T12:00:00Z&" +
               $"uuid=a0eebc99-9c0b-4ef8-bb6d-6bb9bd380a11&" +
               $"json=" + HttpUtility.UrlEncode("""{"test":1}""") + "&" +
               $"jsonb=" + HttpUtility.UrlEncode("""{"test":2}""") + "&" +
               $"intArray=" + HttpUtility.UrlEncode("{1,2,3}") + "&" +
               $"textArray=" + HttpUtility.UrlEncode("""{"x","y"}""");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _client.Dispose();
    }

    /// <summary>
    /// Benchmark: Full-type endpoint with parameterized row count
    /// Measures how serialization time scales with data volume for all PostgreSQL types
    /// </summary>
    [Benchmark]
    public async Task<string> PerfTest_AllTypes_Scaling()
    {
        var urlIndex = RowCount switch
        {
            10 => 0, 50 => 1, 100 => 2, 250 => 3, 500 => 4, 1000 => 5, _ => 0
        };
        using var result = await _client.GetAsync(_prebuiltUrls[urlIndex]);
        return await result.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// Benchmark: Simple table for scaling comparison
    /// </summary>
    [Benchmark]
    public async Task<string> SimpleTable_Scaling()
    {
        using var result = await _client.GetAsync($"{BaseUrl}/api/case-get-long-table1/?records={RowCount}");
        return await result.Content.ReadAsStringAsync();
    }
}

/// <summary>
/// Concurrent request benchmarks to test throughput under load.
/// </summary>
[MemoryDiagnoser]
public class HttpConcurrencyBenchmarks
{
    private HttpClient _client = null!;
    private const string BaseUrl = "http://localhost:5000";
    private string _perfTestUrl = null!;

    [Params(1, 5, 10)]
    public int ConcurrentRequests { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _client = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        _perfTestUrl = $"{BaseUrl}/api/perf-test?" +
                       $"records=50&" +
                       $"text=ConcurrentTest&" +
                       $"int=1&bigint=1&numeric=1.0&real=1.0&double=1.0&" +
                       $"bool=true&date=2024-01-01&" +
                       $"timestamp=2024-01-01T00:00:00&timestamptz=2024-01-01T00:00:00Z&" +
                       $"uuid=00000000-0000-0000-0000-000000000001&" +
                       $"json=" + HttpUtility.UrlEncode("{}") + "&" +
                       $"jsonb=" + HttpUtility.UrlEncode("{}") + "&" +
                       $"intArray=" + HttpUtility.UrlEncode("{1}") + "&" +
                       $"textArray=" + HttpUtility.UrlEncode("""{"a"}""");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _client.Dispose();
    }

    /// <summary>
    /// Benchmark: Concurrent GET requests to full-type endpoint
    /// Tests throughput under concurrent load
    /// </summary>
    [Benchmark]
    public async Task<int> ConcurrentPerfTestRequests()
    {
        var tasks = new Task<HttpResponseMessage>[ConcurrentRequests];
        for (int i = 0; i < ConcurrentRequests; i++)
        {
            tasks[i] = _client.GetAsync(_perfTestUrl);
        }

        var results = await Task.WhenAll(tasks);
        int successCount = 0;
        foreach (var response in results)
        {
            if (response.IsSuccessStatusCode) successCount++;
            response.Dispose();
        }
        return successCount;
    }

    /// <summary>
    /// Benchmark: Concurrent GET to simple table endpoint
    /// </summary>
    [Benchmark]
    public async Task<int> ConcurrentSimpleTableRequests()
    {
        var tasks = new Task<HttpResponseMessage>[ConcurrentRequests];
        for (int i = 0; i < ConcurrentRequests; i++)
        {
            tasks[i] = _client.GetAsync($"{BaseUrl}/api/case-get-long-table1/?records=50");
        }

        var results = await Task.WhenAll(tasks);
        int successCount = 0;
        foreach (var response in results)
        {
            if (response.IsSuccessStatusCode) successCount++;
            response.Dispose();
        }
        return successCount;
    }
}

/// <summary>
/// Type-specific endpoint benchmarks to test different PostgreSQL type serialization paths.
/// Compares full-type endpoint vs simple endpoints to measure type overhead.
/// </summary>
[MemoryDiagnoser]
public class HttpTypeSerializationBenchmarks
{
    private HttpClient _client = null!;
    private const string BaseUrl = "http://localhost:5000";
    private string _perfTestUrl = null!;

    [GlobalSetup]
    public void Setup()
    {
        _client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        _perfTestUrl = $"{BaseUrl}/api/perf-test?" +
                       $"records=100&" +
                       $"text=TypeTest&" +
                       $"int=42&bigint=123456789&numeric=99.99&real=3.14&double=2.718&" +
                       $"bool=true&date=2024-03-15&" +
                       $"timestamp=2024-03-15T14:30:00&timestamptz=2024-03-15T14:30:00Z&" +
                       $"uuid=12345678-1234-1234-1234-123456789abc&" +
                       $"json=" + HttpUtility.UrlEncode("""{"field":"value"}""") + "&" +
                       $"jsonb=" + HttpUtility.UrlEncode("""{"nested":{"field":"value"}}""") + "&" +
                       $"intArray=" + HttpUtility.UrlEncode("{10,20,30,40,50}") + "&" +
                       $"textArray=" + HttpUtility.UrlEncode("""{"one","two","three"}""");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _client.Dispose();
    }

    /// <summary>
    /// Benchmark: Simple types only (int + text) as baseline
    /// Tests: TypeCategory.Numeric and TypeCategory.Text serialization
    /// </summary>
    [Benchmark(Baseline = true)]
    public async Task<string> SimpleTypes_IntText_100Rows()
    {
        using var result = await _client.GetAsync($"{BaseUrl}/api/case-get-long-table1/?records=100");
        return await result.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// Benchmark: All PostgreSQL types
    /// Tests: Full type serialization including datetime, uuid, json, arrays, nullables
    /// </summary>
    [Benchmark]
    public async Task<string> AllTypes_100Rows()
    {
        using var result = await _client.GetAsync(_perfTestUrl);
        return await result.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// Benchmark: Cached set (int + text)
    /// Tests: Cached response path
    /// </summary>
    [Benchmark]
    public async Task<string> CachedIntText_100Rows()
    {
        using var result = await _client.GetAsync($"{BaseUrl}/api/cache-get-set/?count=100");
        return await result.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// Benchmark: Set with null values
    /// Tests: Null handling in serialization
    /// </summary>
    [Benchmark]
    public async Task<string> SetWithNulls_100Rows()
    {
        using var result = await _client.GetAsync($"{BaseUrl}/api/cache-get-set-with-nulls/?count=100");
        return await result.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// Benchmark: JSON type field
    /// Tests: TypeCategory.Json serialization (no escaping needed)
    /// </summary>
    [Benchmark]
    public async Task<string> JsonField()
    {
        using var result = await _client.GetAsync($"{BaseUrl}/api/cache-get-json-record/?key=typetest");
        return await result.Content.ReadAsStringAsync();
    }
}
