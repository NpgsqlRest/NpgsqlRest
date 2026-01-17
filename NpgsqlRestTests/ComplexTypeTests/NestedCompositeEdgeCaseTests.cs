namespace NpgsqlRestTests;

public static partial class Database
{
    public static void NestedCompositeEdgeCaseTests()
    {
        script.Append(@"
        -- =====================================================
        -- EDGE CASE TESTS for nested composite type resolution
        -- =====================================================

        -- Define our own base types for self-contained tests
        create type nce_base_type as (
            id int,
            name text
        );

        create type nce_nested_type as (
            label text,
            inner_val nce_base_type
        );

        create type nce_with_array as (
            group_name text,
            members nce_base_type[]
        );

        -- 4-level deep types for null level tests
        create type nce_level1 as (id int, value text);
        create type nce_level2 as (name text, inner1 nce_level1);
        create type nce_level3 as (label text, inner2 nce_level2);
        create type nce_level4 as (tag text, inner3 nce_level3);

        -- Type with various PostgreSQL types for type diversity tests
        create type nce_diverse_type as (
            id int,
            uuid_val uuid,
            ts_val timestamp,
            tstz_val timestamptz,
            json_val json,
            jsonb_val jsonb,
            bool_val boolean,
            numeric_val numeric(20,5),
            bytea_val bytea
        );

        -- Type for empty string vs NULL distinction test
        create type nce_string_type as (
            id int,
            value text
        );

        -- Type with unicode support
        create type nce_unicode_type as (
            id int,
            emoji text,
            chinese text,
            arabic text
        );

        -- 1. Empty array of composites
        create function get_empty_composite_array()
        returns table(data nce_base_type[])
        language sql as
        $$
        select array[]::nce_base_type[];
        $$;

        -- 2. Composite with all NULL fields
        create function get_all_null_composite()
        returns table(data nce_base_type[])
        language sql as
        $$
        select array[
            row(null, null)::nce_base_type
        ];
        $$;

        -- 3. Mixed NULL composites in array (some null elements, some with null fields)
        create function get_mixed_null_composites()
        returns table(data nce_base_type[])
        language sql as
        $$
        select array[
            row(1, 'first')::nce_base_type,
            null::nce_base_type,
            row(null, 'third')::nce_base_type,
            row(3, null)::nce_base_type,
            null::nce_base_type
        ];
        $$;

        -- 4. Empty string vs NULL - should serialize differently
        create function get_empty_string_vs_null()
        returns table(data nce_string_type[])
        language sql as
        $$
        select array[
            row(1, '')::nce_string_type,
            row(2, null)::nce_string_type,
            row(3, 'has value')::nce_string_type
        ];
        $$;

        -- 5. Unicode characters (emojis, Chinese, Arabic, RTL)
        create function get_unicode_composites()
        returns table(data nce_unicode_type[])
        language sql as
        $$
        select array[
            row(1, '😀🎉🚀', '你好世界', 'مرحبا')::nce_unicode_type,
            row(2, '❤️💙💚', '中文测试', 'العربية')::nce_unicode_type
        ];
        $$;

        -- 6. Diverse PostgreSQL types within composite
        create function get_diverse_type_composite()
        returns table(data nce_diverse_type[])
        language sql as
        $$
        select array[
            row(
                1,
                'a0eebc99-9c0b-4ef8-bb6d-6bb9bd380a11'::uuid,
                '2024-01-15 10:30:00'::timestamp,
                '2024-01-15 10:30:00+00'::timestamptz,
                '{""key"": ""value""}'::json,
                '{""nested"": {""deep"": true}}'::jsonb,
                true,
                12345.67890::numeric(20,5),
                E'\\x48656c6c6f'::bytea
            )::nce_diverse_type
        ];
        $$;

        -- 7. Numeric edge cases (MIN/MAX values)
        create type nce_numeric_extremes as (
            big_int bigint,
            small_int smallint,
            regular_int int,
            decimal_val numeric(15,5)
        );

        create function get_numeric_extremes()
        returns table(data nce_numeric_extremes[])
        language sql as
        $$
        select array[
            row(9223372036854775807::bigint, 32767::smallint, 2147483647, 1234567890.12345::numeric(15,5))::nce_numeric_extremes,
            row(-9223372036854775807::bigint, (-32767)::smallint, -2147483647, -1234567890.12345::numeric(15,5))::nce_numeric_extremes
        ];
        $$;

        -- 8. Very long string in composite
        create function get_long_string_composite()
        returns table(data nce_string_type[])
        language sql as
        $$
        select array[
            row(1, repeat('a', 10000))::nce_string_type
        ];
        $$;

        -- 9. Nested composite with empty array field
        create function get_nested_with_empty_array()
        returns table(data nce_with_array[])
        language sql as
        $$
        select array[
            row('empty_group', array[]::nce_base_type[])::nce_with_array,
            row('has_members', array[row(1,'a')::nce_base_type])::nce_with_array
        ];
        $$;

        -- 10. Boolean edge cases in composites
        create type nce_bool_type as (
            id int,
            flag boolean
        );

        create function get_boolean_composites()
        returns table(data nce_bool_type[])
        language sql as
        $$
        select array[
            row(1, true)::nce_bool_type,
            row(2, false)::nce_bool_type,
            row(3, null)::nce_bool_type
        ];
        $$;

        -- 11. Deep nested with all-null intermediate level
        create function get_deep_nested_with_null_level()
        returns table(data nce_level4[])
        language sql as
        $$
        select array[
            row('top', null)::nce_level4,
            row('another', row('l3', row('l2', null)::nce_level2)::nce_level3)::nce_level4
        ];
        $$;

        -- 12. Single element array vs multi-element
        create function get_single_element_array()
        returns table(data nce_base_type[])
        language sql as
        $$
        select array[row(42, 'only')::nce_base_type];
        $$;
");
    }
}

/// <summary>
/// Edge case tests for nested composite type resolution with ResolveNestedCompositeTypes = true.
/// These tests cover scenarios that might cause issues:
/// - Empty arrays
/// - All-NULL composites
/// - Empty string vs NULL distinction
/// - Unicode characters
/// - Various PostgreSQL types
/// - Numeric edge cases
/// - Very long strings
/// </summary>
[Collection("NestedCompositeFixture")]
public class NestedCompositeEdgeCaseTests(NestedCompositeFixture test)
{
    /// <summary>
    /// Empty array of composite types should serialize as empty JSON array.
    /// </summary>
    [Fact]
    public async Task Test_empty_composite_array()
    {
        using var response = await test.Client.GetAsync("/api/get-empty-composite-array/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"data\":[]}]");
    }

    /// <summary>
    /// Composite with all NULL fields should serialize with null values.
    /// </summary>
    [Fact]
    public async Task Test_all_null_composite()
    {
        using var response = await test.Client.GetAsync("/api/get-all-null-composite/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"data\":[{\"id\":null,\"name\":null}]}]");
    }

    /// <summary>
    /// Array with mixed NULL elements and composites with null fields.
    /// NULL composite elements should serialize as JSON null.
    /// </summary>
    [Fact]
    public async Task Test_mixed_null_composites()
    {
        using var response = await test.Client.GetAsync("/api/get-mixed-null-composites/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"data\":[{\"id\":1,\"name\":\"first\"},null,{\"id\":null,\"name\":\"third\"},{\"id\":3,\"name\":null},null]}]");
    }

    /// <summary>
    /// Empty string should serialize as "" while NULL should serialize as null.
    /// Note: PostgreSQL represents empty string in composite as "" (with quotes) in tuple format.
    /// </summary>
    [Fact]
    public async Task Test_empty_string_vs_null()
    {
        using var response = await test.Client.GetAsync("/api/get-empty-string-vs-null/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify valid JSON
        var action = () => System.Text.Json.JsonDocument.Parse(content);
        action.Should().NotThrow("Empty string vs null should produce valid JSON");

        // Parse and verify
        var doc = System.Text.Json.JsonDocument.Parse(content);
        var data = doc.RootElement[0].GetProperty("data");

        // First element has empty string value
        var first = data[0];
        first.GetProperty("id").GetInt32().Should().Be(1);
        var firstValue = first.GetProperty("value");
        firstValue.ValueKind.Should().Be(System.Text.Json.JsonValueKind.String, "Empty string should be a string, not null");

        // Second element has NULL value
        var second = data[1];
        second.GetProperty("id").GetInt32().Should().Be(2);
        second.GetProperty("value").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null, "NULL should serialize as JSON null");

        // Third element has regular value
        var third = data[2];
        third.GetProperty("id").GetInt32().Should().Be(3);
        third.GetProperty("value").GetString().Should().Be("has value");
    }

    /// <summary>
    /// Unicode characters (emojis, Chinese, Arabic) should be preserved.
    /// </summary>
    [Fact]
    public async Task Test_unicode_characters()
    {
        using var response = await test.Client.GetAsync("/api/get-unicode-composites/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify valid JSON
        var action = () => System.Text.Json.JsonDocument.Parse(content);
        action.Should().NotThrow("Unicode content should produce valid JSON");

        // Parse and verify the structure contains expected Unicode
        var doc = System.Text.Json.JsonDocument.Parse(content);
        var data = doc.RootElement[0].GetProperty("data");

        var first = data[0];
        first.GetProperty("emoji").GetString().Should().Contain("😀");
        first.GetProperty("chinese").GetString().Should().Be("你好世界");
        first.GetProperty("arabic").GetString().Should().Be("مرحبا");
    }

    /// <summary>
    /// Various PostgreSQL types (UUID, timestamp, JSON, bytea, etc.) within composite.
    /// </summary>
    [Fact]
    public async Task Test_diverse_postgresql_types()
    {
        using var response = await test.Client.GetAsync("/api/get-diverse-type-composite/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify valid JSON
        var action = () => System.Text.Json.JsonDocument.Parse(content);
        action.Should().NotThrow("Diverse types should produce valid JSON");

        // Parse and verify structure
        var doc = System.Text.Json.JsonDocument.Parse(content);
        var data = doc.RootElement[0].GetProperty("data");
        var item = data[0];

        item.GetProperty("id").GetInt32().Should().Be(1);
        item.GetProperty("uuidVal").GetString().Should().Be("a0eebc99-9c0b-4ef8-bb6d-6bb9bd380a11");
        item.GetProperty("boolVal").GetBoolean().Should().BeTrue();
    }

    /// <summary>
    /// Numeric extremes (MIN/MAX values for bigint, int, etc.).
    /// </summary>
    [Fact]
    public async Task Test_numeric_extremes()
    {
        using var response = await test.Client.GetAsync("/api/get-numeric-extremes/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify valid JSON
        var action = () => System.Text.Json.JsonDocument.Parse(content);
        action.Should().NotThrow($"Numeric extremes should produce valid JSON. Actual content: {content}");

        // Parse and verify structure
        var doc = System.Text.Json.JsonDocument.Parse(content);
        var data = doc.RootElement[0].GetProperty("data");

        // Max values
        var max = data[0];
        max.GetProperty("bigInt").GetInt64().Should().Be(9223372036854775807);
        max.GetProperty("smallInt").GetInt16().Should().Be(32767);
        max.GetProperty("regularInt").GetInt32().Should().Be(2147483647);

        // Min values (using -MAX instead of MIN due to PostgreSQL literal parsing issues)
        var min = data[1];
        min.GetProperty("bigInt").GetInt64().Should().Be(-9223372036854775807);
        min.GetProperty("smallInt").GetInt16().Should().Be(-32767);
        min.GetProperty("regularInt").GetInt32().Should().Be(-2147483647);
    }

    /// <summary>
    /// Very long string (10000 chars) in composite should be handled correctly.
    /// </summary>
    [Fact]
    public async Task Test_long_string()
    {
        using var response = await test.Client.GetAsync("/api/get-long-string-composite/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify valid JSON
        var action = () => System.Text.Json.JsonDocument.Parse(content);
        action.Should().NotThrow("Long string should produce valid JSON");

        // Parse and verify structure
        var doc = System.Text.Json.JsonDocument.Parse(content);
        var data = doc.RootElement[0].GetProperty("data");
        var value = data[0].GetProperty("value").GetString();

        value.Should().NotBeNull();
        value!.Length.Should().Be(10000);
        value.Should().Be(new string('a', 10000));
    }

    /// <summary>
    /// Nested composite with empty array field.
    /// </summary>
    [Fact]
    public async Task Test_nested_with_empty_array_field()
    {
        using var response = await test.Client.GetAsync("/api/get-nested-with-empty-array/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"data\":[{\"groupName\":\"empty_group\",\"members\":[]},{\"groupName\":\"has_members\",\"members\":[{\"id\":1,\"name\":\"a\"}]}]}]");
    }

    /// <summary>
    /// Boolean values (true, false, null) in composites.
    /// </summary>
    [Fact]
    public async Task Test_boolean_composites()
    {
        using var response = await test.Client.GetAsync("/api/get-boolean-composites/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"data\":[{\"id\":1,\"flag\":true},{\"id\":2,\"flag\":false},{\"id\":3,\"flag\":null}]}]");
    }

    /// <summary>
    /// Deep nesting with NULL at intermediate levels.
    /// </summary>
    [Fact]
    public async Task Test_deep_nested_with_null_level()
    {
        using var response = await test.Client.GetAsync("/api/get-deep-nested-with-null-level/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"data\":[{\"tag\":\"top\",\"inner3\":null},{\"tag\":\"another\",\"inner3\":{\"label\":\"l3\",\"inner2\":{\"name\":\"l2\",\"inner1\":null}}}]}]");
    }

    /// <summary>
    /// Single element array should serialize correctly.
    /// </summary>
    [Fact]
    public async Task Test_single_element_array()
    {
        using var response = await test.Client.GetAsync("/api/get-single-element-array/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"data\":[{\"id\":42,\"name\":\"only\"}]}]");
    }
}
