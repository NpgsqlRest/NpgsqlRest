namespace NpgsqlRestTests.StatsTests;

/// <summary>
/// Integration tests for PostgreSQL Stats endpoints.
/// These tests verify that:
/// - /stats/routines endpoint returns function/procedure statistics
/// - /stats/tables endpoint returns table statistics
/// - /stats/indexes endpoint returns index statistics
/// - /stats/activity endpoint returns current session activity
/// - Stats responses have correct format (JSON and TSV)
/// </summary>
[Collection("StatsTestFixture")]
public class StatsTests(StatsTestFixture test)
{
    /// <summary>
    /// Test that /stats/routines endpoint returns OK status.
    /// </summary>
    [Fact]
    public async Task Routines_Endpoint_Should_Return_Ok()
    {
        // Act
        using var response = await test.Client.GetAsync(StatsTestFixture.RoutinesPath);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "Routines stats endpoint should return 200 OK");
    }

    /// <summary>
    /// Test that /stats/routines endpoint returns valid JSON array.
    /// </summary>
    [Fact]
    public async Task Routines_Endpoint_Should_Return_Json_Array()
    {
        // Act
        using var response = await test.Client.GetAsync(StatsTestFixture.RoutinesPath);
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json",
            "Routines stats should return JSON content type");

        var json = JsonNode.Parse(content);
        json.Should().NotBeNull();
        json!.AsArray().Should().NotBeNull("Response should be a JSON array");
    }

    /// <summary>
    /// Test that /stats/tables endpoint returns OK status.
    /// </summary>
    [Fact]
    public async Task Tables_Endpoint_Should_Return_Ok()
    {
        // Act
        using var response = await test.Client.GetAsync(StatsTestFixture.TablesPath);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "Tables stats endpoint should return 200 OK");
    }

    /// <summary>
    /// Test that /stats/tables endpoint returns valid JSON with expected fields.
    /// </summary>
    [Fact]
    public async Task Tables_Endpoint_Should_Return_Valid_Json_With_Expected_Fields()
    {
        // Act
        using var response = await test.Client.GetAsync(StatsTestFixture.TablesPath);
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        var json = JsonNode.Parse(content);
        json.Should().NotBeNull();
        var array = json!.AsArray();
        array.Should().NotBeNull();

        // Should have at least our test table
        array.Count.Should().BeGreaterThan(0, "Should have at least one table");

        // Check that expected fields exist
        var firstRow = array[0]!.AsObject();
        firstRow.ContainsKey("schema").Should().BeTrue();
        firstRow.ContainsKey("name").Should().BeTrue();
        firstRow.ContainsKey("liveTuples").Should().BeTrue();
        firstRow.ContainsKey("deadTuples").Should().BeTrue();
        firstRow.ContainsKey("totalSize").Should().BeTrue();
    }

    /// <summary>
    /// Test that /stats/indexes endpoint returns OK status.
    /// </summary>
    [Fact]
    public async Task Indexes_Endpoint_Should_Return_Ok()
    {
        // Act
        using var response = await test.Client.GetAsync(StatsTestFixture.IndexesPath);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "Indexes stats endpoint should return 200 OK");
    }

    /// <summary>
    /// Test that /stats/indexes endpoint returns valid JSON with expected fields.
    /// </summary>
    [Fact]
    public async Task Indexes_Endpoint_Should_Return_Valid_Json_With_Expected_Fields()
    {
        // Act
        using var response = await test.Client.GetAsync(StatsTestFixture.IndexesPath);
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        var json = JsonNode.Parse(content);
        json.Should().NotBeNull();
        var array = json!.AsArray();
        array.Should().NotBeNull();

        // Should have at least our test index
        array.Count.Should().BeGreaterThan(0, "Should have at least one index");

        // Check that expected fields exist
        var firstRow = array[0]!.AsObject();
        firstRow.ContainsKey("schema").Should().BeTrue();
        firstRow.ContainsKey("table").Should().BeTrue();
        firstRow.ContainsKey("index").Should().BeTrue();
        firstRow.ContainsKey("isUnique").Should().BeTrue();
        firstRow.ContainsKey("definition").Should().BeTrue();
    }

    /// <summary>
    /// Test that /stats/activity endpoint returns OK status.
    /// </summary>
    [Fact]
    public async Task Activity_Endpoint_Should_Return_Ok()
    {
        // Act
        using var response = await test.Client.GetAsync(StatsTestFixture.ActivityPath);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "Activity stats endpoint should return 200 OK");
    }

    /// <summary>
    /// Test that /stats/activity endpoint returns valid JSON array.
    /// </summary>
    [Fact]
    public async Task Activity_Endpoint_Should_Return_Json_Array()
    {
        // Act
        using var response = await test.Client.GetAsync(StatsTestFixture.ActivityPath);
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json",
            "Activity stats should return JSON content type");

        var json = JsonNode.Parse(content);
        json.Should().NotBeNull();
        json!.AsArray().Should().NotBeNull("Response should be a JSON array");
    }

    /// <summary>
    /// Test that HTML format returns HTML table.
    /// </summary>
    [Fact]
    public async Task Tables_Endpoint_Html_Should_Return_Html_Table()
    {
        // Act
        using var response = await test.Client.GetAsync("/stats/tables-html");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/html",
            "HTML endpoint should return text/html content type");

        // Should have HTML table structure
        content.Should().Contain("<table", "Should contain table element");
        content.Should().Contain("</table>", "Should contain closing table element");
        content.Should().Contain("<th>", "Should contain header cells");
        content.Should().Contain("<td>", "Should contain data cells");
        content.Should().Contain("schema", "Should contain schema column");
        content.Should().Contain("name", "Should contain name column");
    }

    /// <summary>
    /// Test that stats endpoints respond quickly (basic latency test).
    /// </summary>
    [Fact]
    public async Task Stats_Endpoints_Should_Respond_Quickly()
    {
        // Act & Assert - each endpoint should respond within 5 seconds
        var sw = System.Diagnostics.Stopwatch.StartNew();

        using var routinesResponse = await test.Client.GetAsync(StatsTestFixture.RoutinesPath);
        routinesResponse.EnsureSuccessStatusCode();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5),
            "/stats/routines should respond within 5 seconds");

        sw.Restart();
        using var tablesResponse = await test.Client.GetAsync(StatsTestFixture.TablesPath);
        tablesResponse.EnsureSuccessStatusCode();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5),
            "/stats/tables should respond within 5 seconds");

        sw.Restart();
        using var indexesResponse = await test.Client.GetAsync(StatsTestFixture.IndexesPath);
        indexesResponse.EnsureSuccessStatusCode();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5),
            "/stats/indexes should respond within 5 seconds");

        sw.Restart();
        using var activityResponse = await test.Client.GetAsync(StatsTestFixture.ActivityPath);
        activityResponse.EnsureSuccessStatusCode();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5),
            "/stats/activity should respond within 5 seconds");
    }

    /// <summary>
    /// Test that multiple concurrent stats requests work correctly.
    /// </summary>
    [Fact]
    public async Task Concurrent_Stats_Requests_Should_Work()
    {
        // Arrange
        var tasks = new List<Task<HttpResponseMessage>>();

        // Act - send 5 concurrent requests to each endpoint
        for (int i = 0; i < 5; i++)
        {
            tasks.Add(test.Client.GetAsync(StatsTestFixture.RoutinesPath));
            tasks.Add(test.Client.GetAsync(StatsTestFixture.TablesPath));
            tasks.Add(test.Client.GetAsync(StatsTestFixture.IndexesPath));
            tasks.Add(test.Client.GetAsync(StatsTestFixture.ActivityPath));
        }

        var responses = await Task.WhenAll(tasks);

        // Assert - all should succeed
        foreach (var response in responses)
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Dispose();
        }
    }

    /// <summary>
    /// Test that invalid stats path returns 404.
    /// </summary>
    [Fact]
    public async Task Invalid_Stats_Path_Should_Return_NotFound()
    {
        // Act
        using var response = await test.Client.GetAsync("/stats/invalid");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "Invalid stats path should return 404");
    }
}
