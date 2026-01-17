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

        -- =====================================================
        -- DEEP NESTING TEST TYPES (3+ levels)
        -- =====================================================

        -- Level 1: Base type
        create type nc_level1 as (
            id int,
            value text
        );

        -- Level 2: Contains level 1
        create type nc_level2 as (
            name text,
            inner1 nc_level1
        );

        -- Level 3: Contains level 2 (which contains level 1)
        create type nc_level3 as (
            label text,
            inner2 nc_level2
        );

        -- Level 4: Contains level 3 (4 levels deep!)
        create type nc_level4 as (
            tag text,
            inner3 nc_level3
        );

        -- Function returning 4-level deep nested composite
        create function get_4_level_nested()
        returns table(data nc_level4[])
        language sql as
        $$
        select array[
            row(
                'top',
                row(
                    'level3',
                    row(
                        'level2',
                        row(1, 'bottom')::nc_level1
                    )::nc_level2
                )::nc_level3
            )::nc_level4
        ];
        $$;

        -- Type with array of level2 composites (array containing nested composites)
        create type nc_with_level2_array as (
            group_id int,
            items nc_level2[]
        );

        -- Function returning array of composites where each has array of nested composites
        create function get_array_of_nested_composite_arrays()
        returns table(data nc_with_level2_array[])
        language sql as
        $$
        select array[
            row(
                1,
                array[
                    row('a', row(10, 'x')::nc_level1)::nc_level2,
                    row('b', row(20, 'y')::nc_level1)::nc_level2
                ]
            )::nc_with_level2_array,
            row(
                2,
                array[
                    row('c', row(30, 'z')::nc_level1)::nc_level2
                ]
            )::nc_with_level2_array
        ];
        $$;

        -- Type with nested composite that has an array of composites
        create type nc_outer_with_inner_array as (
            id int,
            nested nc_with_composite_array
        );

        -- Function: composite -> composite with array of composites
        create function get_nested_with_inner_array()
        returns table(data nc_outer_with_inner_array[])
        language sql as
        $$
        select array[
            row(
                1,
                row('group1', array[row(1,'a')::nc_inner_type, row(2,'b')::nc_inner_type])::nc_with_composite_array
            )::nc_outer_with_inner_array
        ];
        $$;

        -- Type with array of composites where each has nested composite with array
        create type nc_array_of_nested_with_array as (
            tag text,
            items nc_outer_with_inner_array[]
        );

        -- Function: array of (composite with (composite with array of composites))
        create function get_deeply_nested_arrays()
        returns table(data nc_array_of_nested_with_array[])
        language sql as
        $$
        select array[
            row(
                'top',
                array[
                    row(
                        1,
                        row('g1', array[row(10,'x')::nc_inner_type])::nc_with_composite_array
                    )::nc_outer_with_inner_array,
                    row(
                        2,
                        row('g2', array[row(20,'y')::nc_inner_type, row(30,'z')::nc_inner_type])::nc_with_composite_array
                    )::nc_outer_with_inner_array
                ]
            )::nc_array_of_nested_with_array
        ];
        $$;

        -- Test with NULL values at various nesting levels
        create function get_nested_with_nulls()
        returns table(data nc_level3[])
        language sql as
        $$
        select array[
            row('has_value', row('inner', row(1, 'val')::nc_level1)::nc_level2)::nc_level3,
            row('null_inner2', null)::nc_level3,
            row('null_inner1', row('has_label', null)::nc_level2)::nc_level3
        ];
        $$;

        -- Test with special characters at deep nesting levels
        create function get_nested_with_special_chars()
        returns table(data nc_level3[])
        language sql as
        $$
        select array[
            row('quote""test', row('comma,test', row(1, 'paren(test)')::nc_level1)::nc_level2)::nc_level3,
            row('backslash\\test', row('newline
test', row(2, 'tab	here')::nc_level1)::nc_level2)::nc_level3
        ];
        $$;
");
    }
}

/// <summary>
/// Tests for nested composite type serialization.
/// With ResolveNestedCompositeTypes = true (the default), nested composites and arrays of
/// composites within composite fields are serialized as proper JSON objects/arrays.
///
/// NOTE: 2D composite arrays at the return type level remain as string arrays -
/// this is a known limitation that doesn't affect fields within composites.
///
/// <para><b>Base Types Used by Tests:</b></para>
/// <code>
/// -- Inner type (simple)
/// create type nc_inner_type as (
///     id int,
///     name text
/// );
///
/// -- Outer type containing inner composite
/// create type nc_outer_type as (
///     label text,
///     nested_val nc_inner_type
/// );
///
/// -- Type with array field
/// create type nc_with_array as (
///     id int,
///     items int[]
/// );
///
/// -- Type with array of composites
/// create type nc_with_composite_array as (
///     group_name text,
///     members nc_inner_type[]
/// );
///
/// -- Composite type with 2D array field
/// create type nc_with_2d_array as (
///     id int,
///     matrix int[][]
/// );
/// </code>
/// </summary>
[Collection("TestFixture")]
public class NestedCompositeTypeLimitationsTests(TestFixture test)
{
    /// <summary>
    /// Simple array of composites - properly serialized as JSON array of objects.
    /// <para><b>SQL Function:</b></para>
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
    /// <para><b>Expected JSON:</b></para>
    /// <code>[{"data":[{"id":1,"name":"first"},{"id":2,"name":"second"}]}]</code>
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
    /// Array of composite types where each composite contains another composite.
    /// The nested composite is serialized as a proper JSON object.
    /// <para><b>SQL Function:</b></para>
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
    /// <para><b>Expected JSON:</b></para>
    /// <code>[{"data":[{"label":"a","nestedVal":{"id":1,"name":"x"}},{"label":"b","nestedVal":{"id":2,"name":"y"}}]}]</code>
    /// </summary>
    [Fact]
    public async Task Test_nested_composite_in_array_serialized_as_object()
    {
        using var response = await test.Client.GetAsync("/api/get-nested-composite-array/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        // Nested composite is serialized as JSON object, not as tuple string
        content.Should().Be("[{\"data\":[{\"label\":\"a\",\"nestedVal\":{\"id\":1,\"name\":\"x\"}},{\"label\":\"b\",\"nestedVal\":{\"id\":2,\"name\":\"y\"}}]}]");
    }

    /// <summary>
    /// Array of composite types where each composite contains an array field.
    /// Arrays inside composite types are properly converted to JSON array format.
    /// <para><b>SQL Function:</b></para>
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
    /// <para><b>Expected JSON:</b></para>
    /// <code>[{"data":[{"id":1,"items":[1,2,3]},{"id":2,"items":[4,5,6]}]}]</code>
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
    /// Array of composite types where each composite contains an array of composites.
    /// The members array contains proper JSON objects, not tuple strings.
    /// <para><b>SQL Function:</b></para>
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
    /// <para><b>Expected JSON:</b></para>
    /// <code>[{"data":[{"groupName":"group1","members":[{"id":1,"name":"a"},{"id":2,"name":"b"}]},{"groupName":"group2","members":[{"id":3,"name":"c"}]}]}]</code>
    /// </summary>
    [Fact]
    public async Task Test_composite_array_field_in_composite_serialized_as_object_array()
    {
        using var response = await test.Client.GetAsync("/api/get-composite-with-composite-array/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        // Array of composites inside composite is now JSON array of objects
        content.Should().Be("[{\"data\":[{\"groupName\":\"group1\",\"members\":[{\"id\":1,\"name\":\"a\"},{\"id\":2,\"name\":\"b\"}]},{\"groupName\":\"group2\",\"members\":[{\"id\":3,\"name\":\"c\"}]}]}]");
    }

    /// <summary>
    /// 2D array of composite types at the return type level.
    /// <para><b>KNOWN LIMITATION:</b> 2D composite arrays at the return type level are serialized
    /// as nested arrays of PostgreSQL tuple strings, not as fully expanded JSON objects.
    /// This limitation only affects multidimensional arrays at the top level, not fields
    /// within composite types.</para>
    /// <para><b>SQL Function:</b></para>
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
    /// <para><b>Expected JSON (tuple strings due to limitation):</b></para>
    /// <code>[{"matrix":[["(1,a)","(2,b)"],["(3,c)","(4,d)"]]}]</code>
    /// </summary>
    [Fact]
    public async Task Test_2d_composite_array_serialized_as_string_arrays()
    {
        using var response = await test.Client.GetAsync("/api/get-2d-composite-array/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        // 2D composite arrays at return type level remain as tuple strings
        content.Should().Be("[{\"matrix\":[[\"(1,a)\",\"(2,b)\"],[\"(3,c)\",\"(4,d)\"]]}]");
    }

    /// <summary>
    /// Array of composite types where each composite contains a 2D array field.
    /// The 2D array inside composite is properly serialized as nested JSON arrays.
    /// <para><b>SQL Function:</b></para>
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
    /// <para><b>Expected JSON:</b></para>
    /// <code>[{"data":[{"id":1,"matrix":[[1,2],[3,4]]},{"id":2,"matrix":[[5,6],[7,8]]}]}]</code>
    /// </summary>
    [Fact]
    public async Task Test_composite_with_2d_array_field()
    {
        using var response = await test.Client.GetAsync("/api/get-composite-with-2d-array/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"data\":[{\"id\":1,\"matrix\":[[1,2],[3,4]]},{\"id\":2,\"matrix\":[[5,6],[7,8]]}]}]");
    }
}

