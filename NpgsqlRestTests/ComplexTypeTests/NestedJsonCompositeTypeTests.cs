namespace NpgsqlRestTests;

public static partial class Database
{
    public static void NestedJsonCompositeTypeTests()
    {
        script.Append(@"

        -- Composite type for nested JSON tests
        create type nested_request as (
            id int,
            text_value text,
            flag boolean
        );

        -- Composite type with more fields for testing
        create type nested_complex as (
            code text,
            amount numeric,
            active boolean,
            created_at timestamp
        );

        -- Function returning single composite type
        -- nested
        create function get_nested_single(
            request nested_request
        )
        returns nested_request
        language sql as
        $$
        select request;
        $$;
        comment on function get_nested_single(nested_request) is 'nested';

        -- Function returning setof composite type
        create function get_nested_setof(
            request nested_request
        )
        returns setof nested_request
        language sql as
        $$
        select request union all select request;
        $$;
        comment on function get_nested_setof(nested_request) is 'nested';

        -- Function returning table with only composite type column
        create function get_nested_table_only(
            request nested_request
        )
        returns table (
            req nested_request
        )
        language sql as
        $$
        select request union all select request;
        $$;
        comment on function get_nested_table_only(nested_request) is 'nested';

        -- Function returning table with mixed columns: text, composite, text
        create function get_nested_mixed_table(
            request nested_request
        )
        returns table (
            a text,
            req nested_request,
            b text
        )
        language sql as
        $$
        select 'a1', request, 'b1'
        union all
        select 'a2', request, 'b2'
        $$;
        comment on function get_nested_mixed_table(nested_request) is 'nested';

        -- Function returning table with composite type at the start
        create function get_nested_composite_first(
            request nested_request
        )
        returns table (
            req nested_request,
            a text,
            b int
        )
        language sql as
        $$
        select request, 'value1', 100
        union all
        select request, 'value2', 200
        $$;
        comment on function get_nested_composite_first(nested_request) is 'nested';

        -- Function returning table with composite type at the end
        create function get_nested_composite_last(
            request nested_request
        )
        returns table (
            a text,
            b int,
            req nested_request
        )
        language sql as
        $$
        select 'value1', 100, request
        union all
        select 'value2', 200, request
        $$;
        comment on function get_nested_composite_last(nested_request) is 'nested';

        -- Function returning table with multiple composite type columns
        create function get_nested_multiple_composites(
            req1 nested_request,
            req2 nested_complex
        )
        returns table (
            first_req nested_request,
            middle_text text,
            second_req nested_complex
        )
        language sql as
        $$
        select req1, 'middle', req2;
        $$;
        comment on function get_nested_multiple_composites(nested_request, nested_complex) is 'nested';

        -- Function returning table with two adjacent composite type columns
        create function get_nested_adjacent_composites(
            req1 nested_request,
            req2 nested_request
        )
        returns table (
            first nested_request,
            second nested_request
        )
        language sql as
        $$
        select req1, req2;
        $$;
        comment on function get_nested_adjacent_composites(nested_request, nested_request) is 'nested';

        -- Function returning table with nullable composite type
        create function get_nested_nullable(
            include_data boolean
        )
        returns table (
            a text,
            req nested_request,
            b text
        )
        language sql as
        $$
        select
            'prefix',
            case when include_data then row(1, 'test', true)::nested_request else null end,
            'suffix';
        $$;
        comment on function get_nested_nullable(boolean) is 'nested';
");
    }
}

/// <summary>
/// Tests for nested JSON serialization of composite types.
/// When the NestedJsonForCompositeTypes option is enabled via the 'nested' comment annotation,
/// composite type columns should be serialized as nested JSON objects instead of flattened fields.
///
/// Current (flat): {"a":"a1","id":1,"textValue":"test","flag":true,"b":"b1"}
/// Expected (nested): {"a":"a1","req":{"id":1,"textValue":"test","flag":true},"b":"b1"}
///
/// These tests use the 'nested' comment annotation to enable nested JSON output per-function.
/// </summary>
[Collection("TestFixture")]
public class NestedJsonCompositeTypeTests(TestFixture test)
{
    [Fact]
    public async Task Test_get_nested_single_returns_nested_object()
    {
        // Single composite type return should still be a flat object (no wrapping needed)
        var query = new QueryBuilder
        {
            { "requestId", "1" },
            { "requestTextValue", "test" },
            { "requestFlag", "true" },
        };
        using var response = await test.Client.GetAsync($"/api/get-nested-single/{query}");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        // Single composite type returns as flat object (same as current behavior)
        content.Should().Be("{\"id\":1,\"textValue\":\"test\",\"flag\":true}");
    }

    [Fact]
    public async Task Test_get_nested_setof_returns_nested_array()
    {
        // Setof composite type should return array of flat objects (no wrapping needed)
        var query = new QueryBuilder
        {
            { "requestId", "1" },
            { "requestTextValue", "test" },
            { "requestFlag", "true" },
        };
        using var response = await test.Client.GetAsync($"/api/get-nested-setof/{query}");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        // Setof returns array of flat objects (same as current behavior)
        content.Should().Be("[{\"id\":1,\"textValue\":\"test\",\"flag\":true},{\"id\":1,\"textValue\":\"test\",\"flag\":true}]");
    }

    [Fact]
    public async Task Test_get_nested_table_only_returns_nested_object()
    {
        // Table with only composite column: should wrap in column name
        // returns table (req nested_request)
        var query = new QueryBuilder
        {
            { "requestId", "1" },
            { "requestTextValue", "test" },
            { "requestFlag", "true" },
        };
        using var response = await test.Client.GetAsync($"/api/get-nested-table-only/{query}");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        // With nested JSON option: composite type column wrapped in "req" object
        content.Should().Be("[{\"req\":{\"id\":1,\"textValue\":\"test\",\"flag\":true}},{\"req\":{\"id\":1,\"textValue\":\"test\",\"flag\":true}}]");
    }

    [Fact]
    public async Task Test_get_nested_mixed_table_returns_nested_structure()
    {
        // Table with mixed columns: text, composite, text
        // returns table (a text, req nested_request, b text)
        var query = new QueryBuilder
        {
            { "requestId", "1" },
            { "requestTextValue", "test" },
            { "requestFlag", "true" },
        };
        using var response = await test.Client.GetAsync($"/api/get-nested-mixed-table/{query}");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        // With nested JSON option: composite type column wrapped in "req" object
        content.Should().Be("[{\"a\":\"a1\",\"req\":{\"id\":1,\"textValue\":\"test\",\"flag\":true},\"b\":\"b1\"},{\"a\":\"a2\",\"req\":{\"id\":1,\"textValue\":\"test\",\"flag\":true},\"b\":\"b2\"}]");
    }

    [Fact]
    public async Task Test_get_nested_composite_first_position()
    {
        // Table with composite column at the start
        // returns table (req nested_request, a text, b int)
        var query = new QueryBuilder
        {
            { "requestId", "1" },
            { "requestTextValue", "test" },
            { "requestFlag", "true" },
        };
        using var response = await test.Client.GetAsync($"/api/get-nested-composite-first/{query}");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        // Composite at start, followed by regular columns
        content.Should().Be("[{\"req\":{\"id\":1,\"textValue\":\"test\",\"flag\":true},\"a\":\"value1\",\"b\":100},{\"req\":{\"id\":1,\"textValue\":\"test\",\"flag\":true},\"a\":\"value2\",\"b\":200}]");
    }

    [Fact]
    public async Task Test_get_nested_composite_last_position()
    {
        // Table with composite column at the end
        // returns table (a text, b int, req nested_request)
        var query = new QueryBuilder
        {
            { "requestId", "1" },
            { "requestTextValue", "test" },
            { "requestFlag", "true" },
        };
        using var response = await test.Client.GetAsync($"/api/get-nested-composite-last/{query}");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        // Regular columns first, composite at end
        content.Should().Be("[{\"a\":\"value1\",\"b\":100,\"req\":{\"id\":1,\"textValue\":\"test\",\"flag\":true}},{\"a\":\"value2\",\"b\":200,\"req\":{\"id\":1,\"textValue\":\"test\",\"flag\":true}}]");
    }

    [Fact]
    public async Task Test_get_nested_multiple_composites()
    {
        // Table with multiple composite columns separated by regular column
        // returns table (first_req nested_request, middle_text text, second_req nested_complex)
        var query = new QueryBuilder
        {
            { "req1Id", "1" },
            { "req1TextValue", "test" },
            { "req1Flag", "true" },
            { "req2Code", "ABC" },
            { "req2Amount", "99.99" },
            { "req2Active", "true" },
            { "req2CreatedAt", "2024-01-15T10:30:00" },
        };
        using var response = await test.Client.GetAsync($"/api/get-nested-multiple-composites/{query}");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        // Each composite column wrapped in its own object
        content.Should().Be("[{\"firstReq\":{\"id\":1,\"textValue\":\"test\",\"flag\":true},\"middleText\":\"middle\",\"secondReq\":{\"code\":\"ABC\",\"amount\":99.99,\"active\":true,\"createdAt\":\"2024-01-15T10:30:00\"}}]");
    }

    [Fact]
    public async Task Test_get_nested_adjacent_composites()
    {
        // Table with two adjacent composite columns (no regular columns between them)
        // returns table (first nested_request, second nested_request)
        var query = new QueryBuilder
        {
            { "req1Id", "1" },
            { "req1TextValue", "first" },
            { "req1Flag", "true" },
            { "req2Id", "2" },
            { "req2TextValue", "second" },
            { "req2Flag", "false" },
        };
        using var response = await test.Client.GetAsync($"/api/get-nested-adjacent-composites/{query}");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        // Two adjacent composite columns, each wrapped in their own object
        content.Should().Be("[{\"first\":{\"id\":1,\"textValue\":\"first\",\"flag\":true},\"second\":{\"id\":2,\"textValue\":\"second\",\"flag\":false}}]");
    }

    [Fact]
    public async Task Test_get_nested_nullable_with_value()
    {
        // Table with nullable composite column - when value is present
        var query = new QueryBuilder
        {
            { "includeData", "true" },
        };
        using var response = await test.Client.GetAsync($"/api/get-nested-nullable/{query}");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        // Composite column has value, wrapped in object
        content.Should().Be("[{\"a\":\"prefix\",\"req\":{\"id\":1,\"textValue\":\"test\",\"flag\":true},\"b\":\"suffix\"}]");
    }

    [Fact]
    public async Task Test_get_nested_nullable_without_value()
    {
        // Table with nullable composite column - when value is null
        // NULL composites are detected by buffering field values and checking if all are NULL
        var query = new QueryBuilder
        {
            { "includeData", "false" },
        };
        using var response = await test.Client.GetAsync($"/api/get-nested-nullable/{query}");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        // Composite column with null value appears as null (not an object with null fields)
        content.Should().Be("[{\"a\":\"prefix\",\"req\":null,\"b\":\"suffix\"}]");
    }
}
