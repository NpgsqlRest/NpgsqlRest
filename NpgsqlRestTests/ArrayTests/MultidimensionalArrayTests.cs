namespace NpgsqlRestTests;

public static partial class Database
{
    public static void MultidimensionalArrayTests()
    {
        script.Append(@"
        -- Multidimensional array tests

        -- 2D array of integers
        create function get_2d_int_array()
        returns table(
            matrix int[][]
        )
        language sql as
        $$
        select array[[1,2,3],[4,5,6]];
        $$;

        -- 2D array of text
        create function get_2d_text_array()
        returns table(
            matrix text[][]
        )
        language sql as
        $$
        select array[['a','b'],['c','d']];
        $$;

        -- 3D array of integers
        create function get_3d_int_array()
        returns table(
            cube int[][][]
        )
        language sql as
        $$
        select array[[[1,2],[3,4]],[[5,6],[7,8]]];
        $$;

        -- 2D array with NULLs
        create function get_2d_array_with_nulls()
        returns table(
            matrix int[][]
        )
        language sql as
        $$
        select array[[1,null,3],[null,5,6]];
        $$;

        -- 2D boolean array
        create function get_2d_bool_array()
        returns table(
            matrix boolean[][]
        )
        language sql as
        $$
        select array[[true,false],[false,true]];
        $$;
");
    }
}

/// <summary>
/// Tests for multidimensional arrays (2D, 3D, etc.).
/// PostgreSQL uses nested braces for multidimensional arrays: {{1,2},{3,4}}
/// These are now properly converted to nested JSON arrays: [[1,2],[3,4]]
/// </summary>
[Collection("TestFixture")]
public class MultidimensionalArrayTests(TestFixture test)
{
    /// <summary>
    /// 2D array of integers.
    /// PostgreSQL format: {{1,2,3},{4,5,6}}
    /// JSON format: [[1,2,3],[4,5,6]]
    /// </summary>
    [Fact]
    public async Task Test_2d_int_array()
    {
        using var response = await test.Client.GetAsync("/api/get-2d-int-array/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"matrix\":[[1,2,3],[4,5,6]]}]");
    }

    /// <summary>
    /// 2D array of text.
    /// PostgreSQL format: {{a,b},{c,d}}
    /// JSON format: [["a","b"],["c","d"]]
    /// </summary>
    [Fact]
    public async Task Test_2d_text_array()
    {
        using var response = await test.Client.GetAsync("/api/get-2d-text-array/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"matrix\":[[\"a\",\"b\"],[\"c\",\"d\"]]}]");
    }

    /// <summary>
    /// 3D array of integers.
    /// PostgreSQL format: {{{1,2},{3,4}},{{5,6},{7,8}}}
    /// JSON format: [[[1,2],[3,4]],[[5,6],[7,8]]]
    /// </summary>
    [Fact]
    public async Task Test_3d_int_array()
    {
        using var response = await test.Client.GetAsync("/api/get-3d-int-array/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"cube\":[[[1,2],[3,4]],[[5,6],[7,8]]]}]");
    }

    /// <summary>
    /// 2D array with NULL values.
    /// PostgreSQL format: {{1,NULL,3},{NULL,5,6}}
    /// JSON format: [[1,null,3],[null,5,6]]
    /// </summary>
    [Fact]
    public async Task Test_2d_array_with_nulls()
    {
        using var response = await test.Client.GetAsync("/api/get-2d-array-with-nulls/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"matrix\":[[1,null,3],[null,5,6]]}]");
    }

    /// <summary>
    /// 2D boolean array.
    /// PostgreSQL format: {{t,f},{f,t}}
    /// JSON format: [[true,false],[false,true]]
    /// </summary>
    [Fact]
    public async Task Test_2d_bool_array()
    {
        using var response = await test.Client.GetAsync("/api/get-2d-bool-array/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"matrix\":[[true,false],[false,true]]}]");
    }
}
