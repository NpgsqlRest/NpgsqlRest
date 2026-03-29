namespace NpgsqlRestTests.SqlFileSourceTests;

public static partial class SqlFiles
{
    public static void CompositeTypeNestedTests()
    {
        // Nested mode via @nested annotation: composite as nested object
        File.WriteAllText(Path.Combine(Dir, "composite_nested_mixed.sql"), """
            -- nested
            -- @param $1 id
            select id, data, id + 100 as extra from sql_file_custom_table where id = $1;
            """);

        // Nested mode: composite only column
        File.WriteAllText(Path.Combine(Dir, "composite_nested_only.sql"), """
            -- nested
            -- @param $1 id
            select data from sql_file_custom_table where id = $1;
            """);

        // Nested mode: composite at start
        File.WriteAllText(Path.Combine(Dir, "composite_nested_first.sql"), """
            -- nested
            -- @param $1 id
            select data, id, id + 100 as extra from sql_file_custom_table where id = $1;
            """);

        // Nested mode: composite at end
        File.WriteAllText(Path.Combine(Dir, "composite_nested_last.sql"), """
            -- nested
            -- @param $1 id
            select id, id + 100 as extra, data from sql_file_custom_table where id = $1;
            """);

        // Nested mode: null composite
        File.WriteAllText(Path.Combine(Dir, "composite_nested_null.sql"), """
            -- nested
            -- @param $1 include_data
            select
                'prefix' as a,
                case when $1::boolean then row('hello', 42, true)::sql_file_custom_type else null end as data,
                'suffix' as b;
            """);
    }
}

[Collection("SqlFileSourceFixture")]
public class CompositeTypeNestedTests(SqlFileSourceTestFixture test)
{
    [Fact]
    public async Task NestedMixed_CompositeWrappedInColumnName()
    {
        // SELECT id, data, id + 100 as extra — with @nested
        using var response = await test.Client.GetAsync("/api/composite-nested-mixed?id=1");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"id\":1,\"data\":{\"val1\":\"hello\",\"val2\":42,\"val3\":true},\"extra\":101}]");
    }

    [Fact]
    public async Task NestedOnly_CompositeWrapped()
    {
        // SELECT data — with @nested
        using var response = await test.Client.GetAsync("/api/composite-nested-only?id=1");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"data\":{\"val1\":\"hello\",\"val2\":42,\"val3\":true}}]");
    }

    [Fact]
    public async Task NestedFirst_CompositeAtStart()
    {
        // SELECT data, id, extra — with @nested
        using var response = await test.Client.GetAsync("/api/composite-nested-first?id=1");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"data\":{\"val1\":\"hello\",\"val2\":42,\"val3\":true},\"id\":1,\"extra\":101}]");
    }

    [Fact]
    public async Task NestedLast_CompositeAtEnd()
    {
        // SELECT id, extra, data — with @nested
        using var response = await test.Client.GetAsync("/api/composite-nested-last?id=1");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"id\":1,\"extra\":101,\"data\":{\"val1\":\"hello\",\"val2\":42,\"val3\":true}}]");
    }

    [Fact]
    public async Task NestedNull_NullCompositeAsNull()
    {
        // NULL composite in nested mode → "data": null
        using var response = await test.Client.GetAsync("/api/composite-nested-null?include_data=false");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"a\":\"prefix\",\"data\":null,\"b\":\"suffix\"}]");
    }

    [Fact]
    public async Task NestedNull_WithValue()
    {
        // Non-null composite in nested mode → "data": {object}
        using var response = await test.Client.GetAsync("/api/composite-nested-null?include_data=true");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"a\":\"prefix\",\"data\":{\"val1\":\"hello\",\"val2\":42,\"val3\":true},\"b\":\"suffix\"}]");
    }
}
