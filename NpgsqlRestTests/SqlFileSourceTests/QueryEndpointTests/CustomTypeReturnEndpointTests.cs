using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests.SqlFileSourceTests;

public static partial class SqlFiles
{
    public static void CustomTypeReturnEndpointTests()
    {
        File.WriteAllText(Path.Combine(Dir, "custom_type_return.sql"), """
            -- @param $1 id
            select id, data from sql_file_custom_table where id = $1;
            """);

        File.WriteAllText(Path.Combine(Dir, "custom_array_query.sql"), """
            -- @param $1 id
            select id, items from sql_file_custom_array_table where id = $1;
            """);

        File.WriteAllText(Path.Combine(Dir, "custom_type_fields.sql"), """
            -- @param $1 id
            select id, (data).val1, (data).val2, (data).val3 from sql_file_custom_table where id = $1;
            """);
    }
}

[Collection("SqlFileSourceFixture")]
public class CustomTypeReturnEndpointTests(SqlFileSourceTestFixture test)
{
    [Fact]
    public async Task CustomTypeFields_ReturnsExpandedScalarColumns()
    {
        // SQL: SELECT id, (data).val1, (data).val2, (data).val3 FROM sql_file_custom_table WHERE id = $1
        using var response = await test.Client.GetAsync("/api/custom-type-fields?id=1");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(content);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().Be(1);

        var row = doc.RootElement[0];
        row.GetProperty("id").GetInt32().Should().Be(1);
        row.GetProperty("val1").GetString().Should().Be("hello");
        row.GetProperty("val2").GetInt32().Should().Be(42);
        row.GetProperty("val3").GetBoolean().Should().Be(true);
    }

    [Fact]
    public async Task CustomTypeWholeColumn_ReturnsCompositeAsTupleArray()
    {
        // SQL: SELECT id, data FROM sql_file_custom_table WHERE id = $1
        // Whole composite columns are returned as tuple arrays.
        // For proper field expansion, use: SELECT (data).val1, (data).val2 in your SQL.
        using var response = await test.Client.GetAsync("/api/custom-type-return?id=1");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().NotBeNullOrEmpty();

        using var doc = JsonDocument.Parse(content);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().Be(1);

        var row = doc.RootElement[0];
        row.GetProperty("id").GetInt32().Should().Be(1);
        // Composite column is present — rendered as tuple array
        row.TryGetProperty("data", out _).Should().BeTrue();
        content.Should().Contain("hello");
    }

    [Fact]
    public async Task ArrayOfCustomTypes_ReturnsJsonObjectArray()
    {
        // SQL: SELECT id, items FROM sql_file_custom_array_table WHERE id = $1
        // items is sql_file_custom_type[] — should render as JSON array of objects
        using var response = await test.Client.GetAsync("/api/custom-array-query?id=1");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().NotBeNullOrEmpty();

        using var doc = JsonDocument.Parse(content);
        doc.RootElement.GetArrayLength().Should().Be(1);
        var row = doc.RootElement[0];
        row.GetProperty("id").GetInt32().Should().Be(1);

        // Array of composite types should render as JSON array of objects
        var items = row.GetProperty("items");
        items.ValueKind.Should().Be(JsonValueKind.Array);
        items.GetArrayLength().Should().Be(2);

        var firstItem = items[0];
        firstItem.ValueKind.Should().Be(JsonValueKind.Object);
        firstItem.GetProperty("val1").GetString().Should().Be("first");
        firstItem.GetProperty("val2").GetInt32().Should().Be(10);
        firstItem.GetProperty("val3").GetBoolean().Should().Be(true);

        var secondItem = items[1];
        secondItem.GetProperty("val1").GetString().Should().Be("second");
        secondItem.GetProperty("val2").GetInt32().Should().Be(20);
        secondItem.GetProperty("val3").GetBoolean().Should().Be(false);
    }

    [Fact]
    public async Task CustomTypeFields_NonExistentId_ReturnsEmptyArray()
    {
        using var response = await test.Client.GetAsync("/api/custom-type-fields?id=999");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[]");
    }
}
