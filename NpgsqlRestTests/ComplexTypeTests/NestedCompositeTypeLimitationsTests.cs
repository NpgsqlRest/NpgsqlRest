namespace NpgsqlRestTests;

public static partial class Database
{
    public static void NestedCompositeTypeLimitationsTests()
    {
        script.Append(@"
        -- Tests for nested composite type limitations
        -- These tests document the current behavior when dealing with:
        -- 1. Composite types containing other composite types
        -- 2. Composite types containing arrays
        -- 3. Arrays of composite types containing nested composites
        -- 4. Arrays of composite types containing arrays

        -- Inner type (simple)
        create type nc_inner_type as (
            id int,
            name text
        );

        -- Outer type containing inner composite
        create type nc_outer_type as (
            label text,
            nested_val nc_inner_type
        );

        -- Type with array field
        create type nc_with_array as (
            id int,
            items int[]
        );

        -- Type with array of composites
        create type nc_with_composite_array as (
            group_name text,
            members nc_inner_type[]
        );

        -- 1. Function returning array of composite containing nested composite
        create function get_nested_composite_array()
        returns table(
            data nc_outer_type[]
        )
        language sql as
        $$
        select array[
            row('a', row(1, 'x')::nc_inner_type)::nc_outer_type,
            row('b', row(2, 'y')::nc_inner_type)::nc_outer_type
        ];
        $$;

        -- 2. Function returning array of composite containing array field
        create function get_composite_with_array_field()
        returns table(
            data nc_with_array[]
        )
        language sql as
        $$
        select array[
            row(1, array[1,2,3])::nc_with_array,
            row(2, array[4,5,6])::nc_with_array
        ];
        $$;

        -- 3. Function returning array of composite containing array of composites
        create function get_composite_with_composite_array()
        returns table(
            data nc_with_composite_array[]
        )
        language sql as
        $$
        select array[
            row('group1', array[row(1,'a')::nc_inner_type, row(2,'b')::nc_inner_type])::nc_with_composite_array,
            row('group2', array[row(3,'c')::nc_inner_type])::nc_with_composite_array
        ];
        $$;

        -- 4. Simple array of inner type (should work perfectly)
        create function get_simple_inner_array()
        returns table(
            data nc_inner_type[]
        )
        language sql as
        $$
        select array[
            row(1, 'first')::nc_inner_type,
            row(2, 'second')::nc_inner_type
        ];
        $$;

        -- 5. 2D array of composite types (limitation test)
        create function get_2d_composite_array()
        returns table(
            matrix nc_inner_type[][]
        )
        language sql as
        $$
        select array[[row(1,'a')::nc_inner_type, row(2,'b')::nc_inner_type], [row(3,'c')::nc_inner_type, row(4,'d')::nc_inner_type]];
        $$;

        -- 6. Composite type with 2D array field
        create type nc_with_2d_array as (
            id int,
            matrix int[][]
        );

        create function get_composite_with_2d_array()
        returns table(
            data nc_with_2d_array[]
        )
        language sql as
        $$
        select array[
            row(1, array[[1,2],[3,4]])::nc_with_2d_array,
            row(2, array[[5,6],[7,8]])::nc_with_2d_array
        ];
        $$;
");
    }
}

/// <summary>
/// Tests documenting the current behavior and limitations when dealing with:
/// - Nested composite types (composite inside composite)
/// - Arrays inside composite types
/// - Arrays of composite types containing nested structures
///
/// CURRENT LIMITATIONS:
/// The array composite serialization works for ONE level only.
/// Nested composites and arrays inside composites are serialized as their PostgreSQL string representation.
/// </summary>
[Collection("TestFixture")]
public class NestedCompositeTypeLimitationsTests(TestFixture test)
{
    /// <summary>
    /// Simple array of composites - works perfectly.
    /// This is the supported use case.
    /// </summary>
    [Fact]
    public async Task Test_simple_inner_array_works()
    {
        using var response = await test.Client.GetAsync("/api/get-simple-inner-array/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        // This works: simple composite array is properly serialized
        content.Should().Be("[{\"data\":[{\"id\":1,\"name\":\"first\"},{\"id\":2,\"name\":\"second\"}]}]");
    }

    /// <summary>
    /// Array of composite types where each composite contains another composite.
    /// CURRENT BEHAVIOR: The nested composite is serialized as a string (PostgreSQL format).
    ///
    /// PostgreSQL returns: {"(a,\"(1,x)\")","(b,\"(2,y)\")"}
    /// Current output: nested_val field is a string like "(1,x)" instead of {"id":1,"name":"x"}
    /// </summary>
    [Fact]
    public async Task Test_nested_composite_in_array_serialized_as_string()
    {
        using var response = await test.Client.GetAsync("/api/get-nested-composite-array/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        // Current behavior: nested composite is serialized as string, not as JSON object
        // The nested_val field contains "(1,x)" instead of {"id":1,"name":"x"}
        content.Should().Be("[{\"data\":[{\"label\":\"a\",\"nestedVal\":\"(1,x)\"},{\"label\":\"b\",\"nestedVal\":\"(2,y)\"}]}]");
    }

    /// <summary>
    /// Array of composite types where each composite contains an array field.
    /// Arrays inside composite types are now properly converted to JSON array format.
    ///
    /// PostgreSQL returns: {"(1,\"{1,2,3}\")","(2,\"{4,5,6}\")"}
    /// Output: items field as JSON array [1,2,3]
    /// </summary>
    [Fact]
    public async Task Test_array_field_in_composite_serialized_correctly()
    {
        using var response = await test.Client.GetAsync("/api/get-composite-with-array-field/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        // Arrays inside composite types are properly converted to JSON arrays
        content.Should().Be("[{\"data\":[{\"id\":1,\"items\":[1,2,3]},{\"id\":2,\"items\":[4,5,6]}]}]");
    }

    /// <summary>
    /// Array of composite types where each composite contains an array of composites.
    /// The array is now properly converted to JSON array format, but the composite elements
    /// inside the array are still serialized as PostgreSQL tuple strings (not JSON objects).
    ///
    /// PostgreSQL returns: {"(group1,\"{\"\"(1,a)\"\",\"\"(2,b)\"\"}\")",...}
    /// Current output: members is a JSON array of strings like ["(1,a)","(2,b)"]
    /// IDEAL output would be: members as JSON array of objects [{"id":1,"name":"a"},...]
    /// </summary>
    [Fact]
    public async Task Test_composite_array_field_in_composite_serialized_as_string_array()
    {
        using var response = await test.Client.GetAsync("/api/get-composite-with-composite-array/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        // Array of composites inside composite is now a valid JSON array of strings
        // Each element is a PostgreSQL tuple string like "(1,a)"
        // This is valid JSON but not fully expanded nested objects
        content.Should().Be("[{\"data\":[{\"groupName\":\"group1\",\"members\":[\"(1,a)\",\"(2,b)\"]},{\"groupName\":\"group2\",\"members\":[\"(3,c)\"]}]}]");
    }

    /// <summary>
    /// 2D array of composite types.
    /// PostgreSQL returns: {{"(1,a)","(2,b)"},{"(3,c)","(4,d)"}}
    /// LIMITATION: 2D composite arrays are serialized as nested arrays of PostgreSQL tuple strings,
    /// not as fully expanded JSON objects. The data is preserved but not fully parsed.
    /// </summary>
    [Fact]
    public async Task Test_2d_composite_array_serialized_as_string_arrays()
    {
        using var response = await test.Client.GetAsync("/api/get-2d-composite-array/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        // 2D composite arrays are serialized as nested arrays of tuple strings
        // Each composite element is preserved as "(field1,field2)" string format
        content.Should().Be("[{\"matrix\":[[\"(1,a)\",\"(2,b)\"],[\"(3,c)\",\"(4,d)\"]]}]");
    }

    /// <summary>
    /// Array of composite types where each composite contains a 2D array field.
    /// PostgreSQL returns: {"(1,\"{{1,2},{3,4}}\")","(2,\"{{5,6},{7,8}}\")"}
    /// Now properly converted - the 2D array inside composite is serialized correctly.
    /// </summary>
    [Fact]
    public async Task Test_composite_with_2d_array_field()
    {
        using var response = await test.Client.GetAsync("/api/get-composite-with-2d-array/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        // 2D array inside composite type is now properly serialized as nested JSON arrays
        content.Should().Be("[{\"data\":[{\"id\":1,\"matrix\":[[1,2],[3,4]]},{\"id\":2,\"matrix\":[[5,6],[7,8]]}]}]");
    }
}

