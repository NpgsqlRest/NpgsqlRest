namespace NpgsqlRestTests.HealthChecksTests;

/// <summary>
/// Integration tests for Health Checks endpoints.
/// These tests verify that:
/// - /health endpoint returns overall health status
/// - /health/ready endpoint checks database connectivity (readiness probe)
/// - /health/live endpoint always returns healthy (liveness probe)
/// - Health check responses have correct format
/// </summary>
[Collection("HealthChecksTestFixture")]
public class HealthChecksTests(HealthChecksTestFixture test)
{
    /// <summary>
    /// Test that /health endpoint returns OK status.
    /// </summary>
    [Fact]
    public async Task Health_Endpoint_Should_Return_Ok()
    {
        // Act
        using var response = await test.Client.GetAsync(HealthChecksTestFixture.HealthPath);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "Health endpoint should return 200 OK when healthy");
    }

    /// <summary>
    /// Test that /health endpoint returns "Healthy" text.
    /// </summary>
    [Fact]
    public async Task Health_Endpoint_Should_Return_Healthy_Text()
    {
        // Act
        using var response = await test.Client.GetAsync(HealthChecksTestFixture.HealthPath);
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        content.Should().Be("Healthy",
            "Health endpoint should return 'Healthy' text when all checks pass");
    }

    /// <summary>
    /// Test that /health/ready (readiness probe) endpoint returns OK when database is available.
    /// </summary>
    [Fact]
    public async Task Ready_Endpoint_Should_Return_Ok_When_Database_Available()
    {
        // Act
        using var response = await test.Client.GetAsync(HealthChecksTestFixture.ReadyPath);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "Ready endpoint should return 200 OK when database is available");
    }

    /// <summary>
    /// Test that /health/ready endpoint returns "Healthy" text.
    /// </summary>
    [Fact]
    public async Task Ready_Endpoint_Should_Return_Healthy_Text()
    {
        // Act
        using var response = await test.Client.GetAsync(HealthChecksTestFixture.ReadyPath);
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        content.Should().Be("Healthy",
            "Ready endpoint should return 'Healthy' text when database check passes");
    }

    /// <summary>
    /// Test that /health/live (liveness probe) endpoint always returns OK.
    /// Liveness probe should always succeed if the app is running.
    /// </summary>
    [Fact]
    public async Task Live_Endpoint_Should_Always_Return_Ok()
    {
        // Act
        using var response = await test.Client.GetAsync(HealthChecksTestFixture.LivePath);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "Live endpoint should always return 200 OK if application is running");
    }

    /// <summary>
    /// Test that /health/live endpoint returns "Healthy" text.
    /// </summary>
    [Fact]
    public async Task Live_Endpoint_Should_Return_Healthy_Text()
    {
        // Act
        using var response = await test.Client.GetAsync(HealthChecksTestFixture.LivePath);
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        content.Should().Be("Healthy",
            "Live endpoint should return 'Healthy' text (liveness always succeeds)");
    }

    /// <summary>
    /// Test that health check endpoints respond quickly (basic latency test).
    /// </summary>
    [Fact]
    public async Task Health_Endpoints_Should_Respond_Quickly()
    {
        // Act & Assert - each endpoint should respond within 5 seconds
        var sw = System.Diagnostics.Stopwatch.StartNew();

        using var healthResponse = await test.Client.GetAsync(HealthChecksTestFixture.HealthPath);
        healthResponse.EnsureSuccessStatusCode();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5),
            "/health should respond within 5 seconds");

        sw.Restart();
        using var readyResponse = await test.Client.GetAsync(HealthChecksTestFixture.ReadyPath);
        readyResponse.EnsureSuccessStatusCode();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5),
            "/health/ready should respond within 5 seconds");

        sw.Restart();
        using var liveResponse = await test.Client.GetAsync(HealthChecksTestFixture.LivePath);
        liveResponse.EnsureSuccessStatusCode();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5),
            "/health/live should respond within 5 seconds");
    }

    /// <summary>
    /// Test that health endpoints return plain text content type.
    /// </summary>
    [Fact]
    public async Task Health_Endpoints_Should_Return_PlainText_ContentType()
    {
        // Act
        using var response = await test.Client.GetAsync(HealthChecksTestFixture.HealthPath);

        // Assert
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/plain",
            "Health check endpoints should return text/plain content type");
    }

    /// <summary>
    /// Test that multiple concurrent health check requests work correctly.
    /// </summary>
    [Fact]
    public async Task Concurrent_Health_Checks_Should_Work()
    {
        // Arrange
        var tasks = new List<Task<HttpResponseMessage>>();

        // Act - send 10 concurrent requests to each endpoint
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(test.Client.GetAsync(HealthChecksTestFixture.HealthPath));
            tasks.Add(test.Client.GetAsync(HealthChecksTestFixture.ReadyPath));
            tasks.Add(test.Client.GetAsync(HealthChecksTestFixture.LivePath));
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
    /// Test that invalid health check paths return 404.
    /// </summary>
    [Fact]
    public async Task Invalid_Health_Path_Should_Return_NotFound()
    {
        // Act
        using var response = await test.Client.GetAsync("/health/invalid");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "Invalid health check path should return 404");
    }
}
