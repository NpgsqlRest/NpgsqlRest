using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

/// <summary>
/// Tests for deep nested composite type resolution with ResolveNestedCompositeTypes = true.
/// These tests verify that nested composites are properly serialized as JSON objects
/// instead of PostgreSQL tuple strings.
///
/// SQL definitions for all types and functions are included in each test's documentation
/// for easy review.
/// </summary>
[Collection("NestedCompositeFixture")]
public class DeepNestedCompositeTypeTests(NestedCompositeFixture test)
{
    /*
     * ========================================================================
     * BASE TYPES used by multiple tests:
     * ========================================================================
     *
     * create type nc_inner_type as (id int, name text);
     *
     * create type nc_outer_type as (label text, nested_val nc_inner_type);
     *
     * create type nc_with_array as (id int, items int[]);
     *
     * create type nc_with_composite_array as (group_name text, members nc_inner_type[]);
     *
     * create type nc_with_2d_array as (id int, matrix int[][]);
     * ========================================================================
     */

    /// <summary>
    /// SQL:
    /// <code>
    /// create function get_simple_inner_array()
    /// returns table(data nc_inner_type[])
    /// language sql as $$
    ///     select array[
    ///         row(1, 'first')::nc_inner_type,
    ///         row(2, 'second')::nc_inner_type
    ///     ];
    /// $$;
    /// </code>
    /// </summary>
    [Fact]
    public async Task Test_simple_inner_array_works()
    {
        using var response = await test.Client.GetAsync("/api/get-simple-inner-array/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"data\":[{\"id\":1,\"name\":\"first\"},{\"id\":2,\"name\":\"second\"}]}]");
    }

    /// <summary>
    /// Nested composite inside composite - should be serialized as JSON object.
    ///
    /// SQL:
    /// <code>
    /// create function get_nested_composite_array()
    /// returns table(data nc_outer_type[])
    /// language sql as $$
    ///     select array[
    ///         row('a', row(1, 'x')::nc_inner_type)::nc_outer_type,
    ///         row('b', row(2, 'y')::nc_inner_type)::nc_outer_type
    ///     ];
    /// $$;
    /// </code>
    ///
    /// Expected: nestedVal is {"id":1,"name":"x"}, not "(1,x)"
    /// </summary>
    [Fact]
    public async Task Test_nested_composite_in_array_serialized_as_object()
    {
        using var response = await test.Client.GetAsync("/api/get-nested-composite-array/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"data\":[{\"label\":\"a\",\"nestedVal\":{\"id\":1,\"name\":\"x\"}},{\"label\":\"b\",\"nestedVal\":{\"id\":2,\"name\":\"y\"}}]}]");
    }

    /// <summary>
    /// Composite with array field (non-composite array).
    ///
    /// SQL:
    /// <code>
    /// create function get_composite_with_array_field()
    /// returns table(data nc_with_array[])
    /// language sql as $$
    ///     select array[
    ///         row(1, array[1,2,3])::nc_with_array,
    ///         row(2, array[4,5,6])::nc_with_array
    ///     ];
    /// $$;
    /// </code>
    /// </summary>
    [Fact]
    public async Task Test_array_field_in_composite_serialized_correctly()
    {
        using var response = await test.Client.GetAsync("/api/get-composite-with-array-field/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"data\":[{\"id\":1,\"items\":[1,2,3]},{\"id\":2,\"items\":[4,5,6]}]}]");
    }

    /// <summary>
    /// Composite containing array of composites - members should be JSON objects.
    ///
    /// SQL:
    /// <code>
    /// create function get_composite_with_composite_array()
    /// returns table(data nc_with_composite_array[])
    /// language sql as $$
    ///     select array[
    ///         row('group1', array[row(1,'a')::nc_inner_type, row(2,'b')::nc_inner_type])::nc_with_composite_array,
    ///         row('group2', array[row(3,'c')::nc_inner_type])::nc_with_composite_array
    ///     ];
    /// $$;
    /// </code>
    ///
    /// Expected: members is [{id:1,name:"a"},{id:2,name:"b"}], not ["(1,a)","(2,b)"]
    /// </summary>
    [Fact]
    public async Task Test_composite_array_field_in_composite_serialized_as_object_array()
    {
        using var response = await test.Client.GetAsync("/api/get-composite-with-composite-array/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"data\":[{\"groupName\":\"group1\",\"members\":[{\"id\":1,\"name\":\"a\"},{\"id\":2,\"name\":\"b\"}]},{\"groupName\":\"group2\",\"members\":[{\"id\":3,\"name\":\"c\"}]}]}]");
    }

    /// <summary>
    /// 2D array of composites at return type level - KNOWN LIMITATION.
    /// 2D composite arrays remain as string arrays (not expanded to JSON objects).
    ///
    /// SQL:
    /// <code>
    /// create function get_2d_composite_array()
    /// returns table(matrix nc_inner_type[][])
    /// language sql as $$
    ///     select array[
    ///         [row(1,'a')::nc_inner_type, row(2,'b')::nc_inner_type],
    ///         [row(3,'c')::nc_inner_type, row(4,'d')::nc_inner_type]
    ///     ];
    /// $$;
    /// </code>
    /// </summary>
    [Fact]
    public async Task Test_2d_composite_array_remains_string_arrays()
    {
        using var response = await test.Client.GetAsync("/api/get-2d-composite-array/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        // 2D composite arrays at return type level remain as tuple strings
        content.Should().Be("[{\"matrix\":[[\"(1,a)\",\"(2,b)\"],[\"(3,c)\",\"(4,d)\"]]}]");
    }

    /// <summary>
    /// Composite with 2D array of primitives (not composites).
    ///
    /// SQL:
    /// <code>
    /// create function get_composite_with_2d_array()
    /// returns table(data nc_with_2d_array[])
    /// language sql as $$
    ///     select array[
    ///         row(1, array[[1,2],[3,4]])::nc_with_2d_array,
    ///         row(2, array[[5,6],[7,8]])::nc_with_2d_array
    ///     ];
    /// $$;
    /// </code>
    /// </summary>
    [Fact]
    public async Task Test_composite_with_2d_array_field()
    {
        using var response = await test.Client.GetAsync("/api/get-composite-with-2d-array/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"data\":[{\"id\":1,\"matrix\":[[1,2],[3,4]]},{\"id\":2,\"matrix\":[[5,6],[7,8]]}]}]");
    }

    // =========================================================================
    // DEEP NESTING TESTS (3+ levels)
    // =========================================================================

    /// <summary>
    /// 4-level deep nested composite chain.
    ///
    /// SQL Types:
    /// <code>
    /// create type nc_level1 as (id int, value text);
    /// create type nc_level2 as (name text, inner1 nc_level1);
    /// create type nc_level3 as (label text, inner2 nc_level2);
    /// create type nc_level4 as (tag text, inner3 nc_level3);
    /// </code>
    ///
    /// SQL Function:
    /// <code>
    /// create function get_4_level_nested()
    /// returns table(data nc_level4[])
    /// language sql as $$
    ///     select array[
    ///         row('top',
    ///             row('level3',
    ///                 row('level2',
    ///                     row(1, 'bottom')::nc_level1
    ///                 )::nc_level2
    ///             )::nc_level3
    ///         )::nc_level4
    ///     ];
    /// $$;
    /// </code>
    ///
    /// Expected: All 4 levels serialized as nested JSON objects.
    /// </summary>
    [Fact]
    public async Task Test_4_level_deep_nested_composite()
    {
        using var response = await test.Client.GetAsync("/api/get-4-level-nested/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"data\":[{\"tag\":\"top\",\"inner3\":{\"label\":\"level3\",\"inner2\":{\"name\":\"level2\",\"inner1\":{\"id\":1,\"value\":\"bottom\"}}}}]}]");
    }

    /// <summary>
    /// Array of composites, each containing array of nested composites.
    ///
    /// SQL Type:
    /// <code>
    /// create type nc_with_level2_array as (group_id int, items nc_level2[]);
    /// -- where nc_level2 = (name text, inner1 nc_level1)
    /// -- and nc_level1 = (id int, value text)
    /// </code>
    ///
    /// SQL Function:
    /// <code>
    /// create function get_array_of_nested_composite_arrays()
    /// returns table(data nc_with_level2_array[])
    /// language sql as $$
    ///     select array[
    ///         row(1, array[
    ///             row('a', row(10, 'x')::nc_level1)::nc_level2,
    ///             row('b', row(20, 'y')::nc_level1)::nc_level2
    ///         ])::nc_with_level2_array,
    ///         row(2, array[
    ///             row('c', row(30, 'z')::nc_level1)::nc_level2
    ///         ])::nc_with_level2_array
    ///     ];
    /// $$;
    /// </code>
    ///
    /// Expected: items[] contains objects with inner1 as nested object.
    /// </summary>
    [Fact]
    public async Task Test_array_of_composites_with_nested_composite_arrays()
    {
        using var response = await test.Client.GetAsync("/api/get-array-of-nested-composite-arrays/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"data\":[{\"groupId\":1,\"items\":[{\"name\":\"a\",\"inner1\":{\"id\":10,\"value\":\"x\"}},{\"name\":\"b\",\"inner1\":{\"id\":20,\"value\":\"y\"}}]},{\"groupId\":2,\"items\":[{\"name\":\"c\",\"inner1\":{\"id\":30,\"value\":\"z\"}}]}]}]");
    }

    /// <summary>
    /// Composite -> nested composite -> array of composites.
    ///
    /// SQL Type:
    /// <code>
    /// create type nc_outer_with_inner_array as (id int, nested nc_with_composite_array);
    /// -- where nc_with_composite_array = (group_name text, members nc_inner_type[])
    /// </code>
    ///
    /// SQL Function:
    /// <code>
    /// create function get_nested_with_inner_array()
    /// returns table(data nc_outer_with_inner_array[])
    /// language sql as $$
    ///     select array[
    ///         row(1,
    ///             row('group1', array[
    ///                 row(1,'a')::nc_inner_type,
    ///                 row(2,'b')::nc_inner_type
    ///             ])::nc_with_composite_array
    ///         )::nc_outer_with_inner_array
    ///     ];
    /// $$;
    /// </code>
    ///
    /// Expected: nested.members[] contains JSON objects.
    /// </summary>
    [Fact]
    public async Task Test_nested_composite_with_inner_array()
    {
        using var response = await test.Client.GetAsync("/api/get-nested-with-inner-array/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"data\":[{\"id\":1,\"nested\":{\"groupName\":\"group1\",\"members\":[{\"id\":1,\"name\":\"a\"},{\"id\":2,\"name\":\"b\"}]}}]}]");
    }

    /// <summary>
    /// Complex: array[] -> composite -> array[] -> composite -> array[] of composites.
    ///
    /// SQL Type:
    /// <code>
    /// create type nc_array_of_nested_with_array as (tag text, items nc_outer_with_inner_array[]);
    /// -- where nc_outer_with_inner_array = (id int, nested nc_with_composite_array)
    /// -- where nc_with_composite_array = (group_name text, members nc_inner_type[])
    /// </code>
    ///
    /// SQL Function:
    /// <code>
    /// create function get_deeply_nested_arrays()
    /// returns table(data nc_array_of_nested_with_array[])
    /// language sql as $$
    ///     select array[
    ///         row('top', array[
    ///             row(1, row('g1', array[row(10,'x')::nc_inner_type])::nc_with_composite_array)::nc_outer_with_inner_array,
    ///             row(2, row('g2', array[row(20,'y')::nc_inner_type, row(30,'z')::nc_inner_type])::nc_with_composite_array)::nc_outer_with_inner_array
    ///         ])::nc_array_of_nested_with_array
    ///     ];
    /// $$;
    /// </code>
    ///
    /// Expected: All nested arrays and composites properly serialized.
    /// </summary>
    [Fact]
    public async Task Test_deeply_nested_arrays()
    {
        using var response = await test.Client.GetAsync("/api/get-deeply-nested-arrays/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"data\":[{\"tag\":\"top\",\"items\":[{\"id\":1,\"nested\":{\"groupName\":\"g1\",\"members\":[{\"id\":10,\"name\":\"x\"}]}},{\"id\":2,\"nested\":{\"groupName\":\"g2\",\"members\":[{\"id\":20,\"name\":\"y\"},{\"id\":30,\"name\":\"z\"}]}}]}]}]");
    }

    /// <summary>
    /// NULL values at various nesting levels.
    ///
    /// SQL Function:
    /// <code>
    /// create function get_nested_with_nulls()
    /// returns table(data nc_level3[])
    /// language sql as $$
    ///     select array[
    ///         row('has_value', row('inner', row(1, 'val')::nc_level1)::nc_level2)::nc_level3,
    ///         row('null_inner2', null)::nc_level3,
    ///         row('null_inner1', row('has_label', null)::nc_level2)::nc_level3
    ///     ];
    /// $$;
    /// </code>
    ///
    /// Expected: null composites serialize as JSON null.
    /// </summary>
    [Fact]
    public async Task Test_nested_with_null_values()
    {
        using var response = await test.Client.GetAsync("/api/get-nested-with-nulls/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"data\":[{\"label\":\"has_value\",\"inner2\":{\"name\":\"inner\",\"inner1\":{\"id\":1,\"value\":\"val\"}}},{\"label\":\"null_inner2\",\"inner2\":null},{\"label\":\"null_inner1\",\"inner2\":{\"name\":\"has_label\",\"inner1\":null}}]}]");
    }

    /// <summary>
    /// Special characters in deeply nested composites.
    ///
    /// SQL Function:
    /// <code>
    /// create function get_nested_with_special_chars()
    /// returns table(data nc_level3[])
    /// language sql as $$
    ///     select array[
    ///         row('quote"test', row('comma,test', row(1, 'paren(test)')::nc_level1)::nc_level2)::nc_level3,
    ///         row('backslash\test', row('newline
    /// test', row(2, 'tab	here')::nc_level1)::nc_level2)::nc_level3
    ///     ];
    /// $$;
    /// </code>
    ///
    /// Expected: Valid JSON with properly escaped special characters.
    /// </summary>
    [Fact]
    public async Task Test_nested_with_special_characters()
    {
        using var response = await test.Client.GetAsync("/api/get-nested-with-special-chars/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify it's valid JSON
        var action = () => System.Text.Json.JsonDocument.Parse(content);
        action.Should().NotThrow("JSON with special characters in nested composites should be valid");

        // Parse and verify the structure
        var doc = System.Text.Json.JsonDocument.Parse(content);
        var data = doc.RootElement[0].GetProperty("data");
        data.GetArrayLength().Should().Be(2);

        // First element has quote and comma tests
        var first = data[0];
        first.GetProperty("label").GetString().Should().Contain("quote");
        first.GetProperty("inner2").GetProperty("name").GetString().Should().Contain("comma");
        first.GetProperty("inner2").GetProperty("inner1").GetProperty("value").GetString().Should().Contain("paren");

        // Second element has backslash, newline, tab tests
        var second = data[1];
        second.GetProperty("label").GetString().Should().Contain("backslash");
        second.GetProperty("inner2").GetProperty("name").GetString().Should().Contain("newline");
        second.GetProperty("inner2").GetProperty("inner1").GetProperty("value").GetString().Should().Contain("tab");
    }
}
