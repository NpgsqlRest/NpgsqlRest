namespace NpgsqlRestTests.SqlFileSourceTests;

public static partial class SqlFiles
{
    public static void SingleColumnSetTests()
    {
        File.WriteAllText(Path.Combine(Dir, "single_col_values.sql"), """
            -- HTTP GET
            select * from (
                values ('Hello'), ('World'), ('Test')
            ) as t(text);
            """);

        File.WriteAllText(Path.Combine(Dir, "single_col_numbers.sql"), """
            -- HTTP GET
            select * from (
                values (1), (2), (3)
            ) as t(num);
            """);

        File.WriteAllText(Path.Combine(Dir, "multi_col_values.sql"), """
            -- HTTP GET
            select * from (
                values ('a', 1), ('b', 2)
            ) as t(name, id);
            """);

        // Column names with JSON-special characters (double-quoted identifiers in PostgreSQL)
        File.WriteAllText(Path.Combine(Dir, "escaped_col_names.sql"), """
            -- HTTP GET
            select 1 as "my""quote", 2 as "back\slash";
            """);
    }
}

[Collection("SqlFileSourceFixture")]
public class SingleColumnSetTests(SqlFileSourceTestFixture test)
{
    [Fact]
    public async Task SingleColumnText_ReturnsFlatArray()
    {
        using var response = await test.Client.GetAsync("/api/single-col-values");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("""["Hello","World","Test"]""");
    }

    [Fact]
    public async Task SingleColumnNumbers_ReturnsFlatArray()
    {
        using var response = await test.Client.GetAsync("/api/single-col-numbers");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("[1,2,3]");
    }

    [Fact]
    public async Task EscapedColumnNames_JsonSpecialChars_ProducesValidJson()
    {
        using var response = await test.Client.GetAsync("/api/escaped-col-names");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();

        // Verify it's valid JSON by parsing it
        var doc = JsonDocument.Parse(content);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task MultiColumn_ReturnsObjectArray()
    {
        using var response = await test.Client.GetAsync("/api/multi-col-values");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("""[{"name":"a","id":1},{"name":"b","id":2}]""");
    }
}
