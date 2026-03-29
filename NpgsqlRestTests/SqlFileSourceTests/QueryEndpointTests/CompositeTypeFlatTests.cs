namespace NpgsqlRestTests.SqlFileSourceTests;

public static partial class SqlFiles
{
    public static void CompositeTypeFlatTests()
    {
        // Composite column only (no sibling scalar columns)
        File.WriteAllText(Path.Combine(Dir, "composite_only.sql"), """
            -- @param $1 id
            select data from sql_file_custom_table where id = $1;
            """);

        // Mixed: scalar, composite, scalar
        File.WriteAllText(Path.Combine(Dir, "composite_mixed.sql"), """
            -- @param $1 id
            select id, data, id + 100 as extra from sql_file_custom_table where id = $1;
            """);

        // Multiple rows with composite
        File.WriteAllText(Path.Combine(Dir, "composite_multi_row.sql"), """
            select id, data from sql_file_custom_table order by id;
            """);

        // NULL composite
        File.WriteAllText(Path.Combine(Dir, "composite_null.sql"), """
            -- @param $1 include_data
            select
                'prefix' as a,
                case when $1::boolean then row('hello', 42, true)::sql_file_custom_type else null end as data,
                'suffix' as b;
            """);
    }
}

[Collection("SqlFileSourceFixture")]
public class CompositeTypeFlatTests(SqlFileSourceTestFixture test)
{
    [Fact]
    public async Task CompositeOnly_ReturnsFlatFields()
    {
        // SELECT data FROM sql_file_custom_table WHERE id = $1
        // Single composite column → flat fields
        using var response = await test.Client.GetAsync("/api/composite-only?id=1");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"val1\":\"hello\",\"val2\":42,\"val3\":true}]");
    }

    [Fact]
    public async Task CompositeMixed_ScalarCompositeScalar()
    {
        // SELECT id, data, id + 100 as extra
        // Flat mode: composite fields spliced inline between id and extra
        using var response = await test.Client.GetAsync("/api/composite-mixed?id=1");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"id\":1,\"val1\":\"hello\",\"val2\":42,\"val3\":true,\"extra\":101}]");
    }

    [Fact]
    public async Task CompositeMultiRow_AllRowsExpanded()
    {
        // SELECT id, data FROM sql_file_custom_table ORDER BY id
        using var response = await test.Client.GetAsync("/api/composite-multi-row");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"id\":1,\"val1\":\"hello\",\"val2\":42,\"val3\":true},{\"id\":2,\"val1\":\"world\",\"val2\":99,\"val3\":false}]");
    }

    [Fact]
    public async Task CompositeNull_AllFieldsNull()
    {
        // NULL composite → all fields emitted as null
        using var response = await test.Client.GetAsync("/api/composite-null?include_data=false");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"a\":\"prefix\",\"val1\":null,\"val2\":null,\"val3\":null,\"b\":\"suffix\"}]");
    }

    [Fact]
    public async Task CompositeNull_WithValue()
    {
        // Non-null composite → flat fields
        using var response = await test.Client.GetAsync("/api/composite-null?include_data=true");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"a\":\"prefix\",\"val1\":\"hello\",\"val2\":42,\"val3\":true,\"b\":\"suffix\"}]");
    }
}
