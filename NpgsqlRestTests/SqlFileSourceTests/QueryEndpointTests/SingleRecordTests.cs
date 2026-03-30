using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests.SqlFileSourceTests;

public static partial class SqlFiles
{
    public static void SingleRecordTests()
    {
        // Single record — multi-column query returns object not array
        File.WriteAllText(Path.Combine(Dir, "sf_single_object.sql"), """
            -- HTTP GET
            -- single
            select 1 as id, 'alice' as name, true as active;
            """);

        // Single record — multi-row query returns only first row
        File.WriteAllText(Path.Combine(Dir, "sf_single_first_row.sql"), """
            -- HTTP GET
            -- @single
            select x as id, 'item_' || x as name
            from generate_series(1, 50) as x;
            """);

        // Single record — single column returns scalar
        File.WriteAllText(Path.Combine(Dir, "sf_single_scalar.sql"), """
            -- HTTP GET
            -- single
            select 'hello_single' as val;
            """);

        // Without single for comparison — returns array
        File.WriteAllText(Path.Combine(Dir, "sf_no_single.sql"), """
            -- HTTP GET
            select 1 as id, 'alice' as name
            union all
            select 2 as id, 'bob' as name;
            """);

        // Single record with single_record alias
        File.WriteAllText(Path.Combine(Dir, "sf_single_record_alias.sql"), """
            -- HTTP GET
            -- single_record
            select 42 as answer, 'life' as topic;
            """);
    }
}

[Collection("SqlFileSourceFixture")]
public class SingleRecordTests(SqlFileSourceTestFixture test)
{
    [Fact]
    public async Task SqlFile_SingleObject_ReturnsJsonObject()
    {
        using var response = await test.Client.GetAsync("/api/sf-single-object");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");
        body.Should().Be("{\"id\":1,\"name\":\"alice\",\"active\":true}");
    }

    [Fact]
    public async Task SqlFile_SingleFirstRow_ReturnsOnlyFirstRow()
    {
        using var response = await test.Client.GetAsync("/api/sf-single-first-row");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");
        body.Should().Be("{\"id\":1,\"name\":\"item_1\"}");
    }

    [Fact]
    public async Task SqlFile_SingleScalar_ReturnsSingleValue()
    {
        using var response = await test.Client.GetAsync("/api/sf-single-scalar");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");
        body.Should().Be("\"hello_single\"");
    }

    [Fact]
    public async Task SqlFile_NoSingle_ReturnsArray()
    {
        using var response = await test.Client.GetAsync("/api/sf-no-single");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");
        body.Should().Be("[{\"id\":1,\"name\":\"alice\"},{\"id\":2,\"name\":\"bob\"}]");
    }

    [Fact]
    public async Task SqlFile_SingleRecordAlias_ReturnsJsonObject()
    {
        using var response = await test.Client.GetAsync("/api/sf-single-record-alias");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");
        body.Should().Be("{\"answer\":42,\"topic\":\"life\"}");
    }
}
