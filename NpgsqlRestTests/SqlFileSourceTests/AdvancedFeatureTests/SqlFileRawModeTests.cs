namespace NpgsqlRestTests.SqlFileSourceTests;

[Collection("SqlFileAdvancedFixture")]
public class SqlFileRawModeTests(SqlFileAdvancedFixture test)
{
    [Fact]
    public async Task SqlFile_RawBasic_ReturnsConcatenatedValues()
    {
        using var response = await test.Client.GetAsync("/api/sf-raw-basic");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");

        // Raw mode concatenates values without JSON formatting
        body.Should().Contain("123");
        body.Should().Contain("hello");
        // Should NOT be JSON array
        body.Should().NotStartWith("[");
    }

    [Fact]
    public async Task SqlFile_RawCsv_ReturnsCommaSeparatedValues()
    {
        using var response = await test.Client.GetAsync("/api/sf-raw-csv");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");

        // CSV format with comma separator
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/csv");
        body.Should().Contain(",");
        body.Should().Contain("123");
        body.Should().Contain("hello");
    }

    [Fact]
    public async Task SqlFile_RawCsvHeaders_IncludesColumnNamesAsFirstRow()
    {
        using var response = await test.Client.GetAsync("/api/sf-raw-csv-headers");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");

        response.Content.Headers.ContentType?.MediaType.Should().Be("text/csv");

        // First row should contain column names
        var lines = body.Split('\n');
        lines.Length.Should().BeGreaterThanOrEqualTo(2);
        lines[0].Should().Contain("n");
        lines[0].Should().Contain("b");
        lines[0].Should().Contain("t");

        // Second row should contain data
        lines[1].Should().Contain("123");
        lines[1].Should().Contain("hello");
    }

    [Fact]
    public async Task SqlFile_RawCsvAtPrefix_SeparatorAndNewLineWorkWithAtPrefix()
    {
        using var response = await test.Client.GetAsync("/api/sf-raw-csv-at-prefix");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");

        response.Content.Headers.ContentType?.MediaType.Should().Be("text/csv");

        // Should have column headers (from @columns) and comma-separated values (from @separator ,)
        var lines = body.Split('\n');
        lines.Length.Should().BeGreaterThanOrEqualTo(2);

        // Header row should contain column names with comma separator
        lines[0].Should().Contain("n");
        lines[0].Should().Contain(",");
        lines[0].Should().Contain("t");

        // Data row should contain values with comma separator
        lines[1].Should().Contain("123");
        lines[1].Should().Contain(",");
        lines[1].Should().Contain("hello");
    }

    [Fact]
    public async Task SqlFile_RawTabAtPrefix_TabSeparatorWorksWithAtPrefix()
    {
        using var response = await test.Client.GetAsync("/api/sf-raw-tab-at-prefix");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");

        // Tab-separated values
        body.Should().Contain("\t");
        body.Should().Contain("123");
        body.Should().Contain("hello");
    }
}
