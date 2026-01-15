namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void TsClientArrayTypeTests()
        {
            script.Append("""
create schema if not exists tsclient_test;

-- Simple type for 2D composite array tests (defined here to ensure order)
create type tsclient_test.simple_item as (
    id int,
    name text
);

-- 1D array (works correctly)
create function tsclient_test.get_simple_int_array()
returns table(
    numbers int[]
)
language sql as
$$
select array[1,2,3];
$$;
comment on function tsclient_test.get_simple_int_array() is '
tsclient_module=array_types
';

-- 2D array of integers (limitation: typed as number[] instead of number[][])
create function tsclient_test.get_2d_int_array()
returns table(
    matrix int[][]
)
language sql as
$$
select array[[1,2,3],[4,5,6]];
$$;
comment on function tsclient_test.get_2d_int_array() is '
tsclient_module=array_types
';

-- 3D array of integers (limitation: typed as number[] instead of number[][][])
create function tsclient_test.get_3d_int_array()
returns table(
    cube int[][][]
)
language sql as
$$
select array[[[1,2],[3,4]],[[5,6],[7,8]]];
$$;
comment on function tsclient_test.get_3d_int_array() is '
tsclient_module=array_types
';

-- 2D array of text (limitation: typed as string[] instead of string[][])
create function tsclient_test.get_2d_text_array()
returns table(
    matrix text[][]
)
language sql as
$$
select array[['a','b'],['c','d']];
$$;
comment on function tsclient_test.get_2d_text_array() is '
tsclient_module=array_types
';

-- 2D array of composite types (limitation: typed as string[] and elements are tuple strings)
create function tsclient_test.get_2d_composite_array()
returns table(
    matrix tsclient_test.simple_item[][]
)
language sql as
$$
select array[
    [row(1,'a')::tsclient_test.simple_item, row(2,'b')::tsclient_test.simple_item],
    [row(3,'c')::tsclient_test.simple_item, row(4,'d')::tsclient_test.simple_item]
];
$$;
comment on function tsclient_test.get_2d_composite_array() is '
tsclient_module=array_types
';
""");
        }
    }
}

namespace NpgsqlRestTests.TsClientTests
{
    /// <summary>
    /// Tests for TsClient array type generation.
    ///
    /// POSTGRESQL LIMITATION (cannot be fixed):
    /// PostgreSQL normalizes multidimensional array types (int[][], int[][][]) to single-dimensional (integer[])
    /// in ALL catalog views including information_schema, pg_proc, pg_type, and pg_get_functiondef.
    /// There is no way to retrieve the original array dimensionality from PostgreSQL metadata.
    ///
    /// CONSEQUENCES FOR TSCLIENT:
    /// - Multidimensional arrays are typed as single-dimensional in TypeScript (number[] instead of number[][])
    /// - Runtime JSON is correct (nested arrays: [[1,2],[3,4]]), but TypeScript types won't match
    /// - 2D composite arrays are typed as IComposite[] but runtime returns nested string arrays
    ///
    /// WORKAROUND:
    /// For strict TypeScript projects, manually cast the response type when using multidimensional arrays.
    /// </summary>
    [Collection("TestFixture")]
    public class ArrayTypeTests
    {
        // Expected TypeScript output for array types
        // LIMITATION: PostgreSQL normalizes multidimensional arrays (int[][], int[][][]) to single-dimensional (integer[])
        // in all catalog views (information_schema, pg_proc, pg_get_functiondef).
        // Therefore, TsClient cannot distinguish between int[] and int[][] - both become number[].
        // The runtime JSON is correct (nested arrays), but TypeScript types show single-dimensional arrays.
        // NOTE: 2D composite arrays are typed as IMatrix[] but runtime returns [["(1,a)","(2,b)"],...]
        private const string Expected = """
const baseUrl = "";

interface IMatrix {
    id: number | null;
    name: string | null;
}

interface ITsclientTestGet2dCompositeArrayResponse {
    matrix: IMatrix[] | null;
}

interface ITsclientTestGet2dIntArrayResponse {
    matrix: number[] | null;
}

interface ITsclientTestGet2dTextArrayResponse {
    matrix: string[] | null;
}

interface ITsclientTestGet3dIntArrayResponse {
    cube: number[] | null;
}

interface ITsclientTestGetSimpleIntArrayResponse {
    numbers: number[] | null;
}


/**
* function tsclient_test.get_2d_composite_array()
* returns table(
*     matrix tsclient_test.simple_item[]
* )
*
* @remarks
* comment on function tsclient_test.get_2d_composite_array is 'tsclient_module=array_types
*
* @returns {ITsclientTestGet2dCompositeArrayResponse[]}
*
* @see FUNCTION tsclient_test.get_2d_composite_array
*/
export async function tsclientTestGet2dCompositeArray() : Promise<ITsclientTestGet2dCompositeArrayResponse[]> {
    const response = await fetch(baseUrl + "/api/tsclient-test/get-2d-composite-array", {
        method: "GET",
        headers: {
            "Content-Type": "application/json"
        },
    });
    return await response.json() as ITsclientTestGet2dCompositeArrayResponse[];
}

/**
* function tsclient_test.get_2d_int_array()
* returns table(
*     matrix integer[]
* )
*
* @remarks
* comment on function tsclient_test.get_2d_int_array is 'tsclient_module=array_types
*
* @returns {ITsclientTestGet2dIntArrayResponse[]}
*
* @see FUNCTION tsclient_test.get_2d_int_array
*/
export async function tsclientTestGet2dIntArray() : Promise<ITsclientTestGet2dIntArrayResponse[]> {
    const response = await fetch(baseUrl + "/api/tsclient-test/get-2d-int-array", {
        method: "GET",
        headers: {
            "Content-Type": "application/json"
        },
    });
    return await response.json() as ITsclientTestGet2dIntArrayResponse[];
}

/**
* function tsclient_test.get_2d_text_array()
* returns table(
*     matrix text[]
* )
*
* @remarks
* comment on function tsclient_test.get_2d_text_array is 'tsclient_module=array_types
*
* @returns {ITsclientTestGet2dTextArrayResponse[]}
*
* @see FUNCTION tsclient_test.get_2d_text_array
*/
export async function tsclientTestGet2dTextArray() : Promise<ITsclientTestGet2dTextArrayResponse[]> {
    const response = await fetch(baseUrl + "/api/tsclient-test/get-2d-text-array", {
        method: "GET",
        headers: {
            "Content-Type": "application/json"
        },
    });
    return await response.json() as ITsclientTestGet2dTextArrayResponse[];
}

/**
* function tsclient_test.get_3d_int_array()
* returns table(
*     cube integer[]
* )
*
* @remarks
* comment on function tsclient_test.get_3d_int_array is 'tsclient_module=array_types
*
* @returns {ITsclientTestGet3dIntArrayResponse[]}
*
* @see FUNCTION tsclient_test.get_3d_int_array
*/
export async function tsclientTestGet3dIntArray() : Promise<ITsclientTestGet3dIntArrayResponse[]> {
    const response = await fetch(baseUrl + "/api/tsclient-test/get-3d-int-array", {
        method: "GET",
        headers: {
            "Content-Type": "application/json"
        },
    });
    return await response.json() as ITsclientTestGet3dIntArrayResponse[];
}

/**
* function tsclient_test.get_simple_int_array()
* returns table(
*     numbers integer[]
* )
*
* @remarks
* comment on function tsclient_test.get_simple_int_array is 'tsclient_module=array_types
*
* @returns {ITsclientTestGetSimpleIntArrayResponse[]}
*
* @see FUNCTION tsclient_test.get_simple_int_array
*/
export async function tsclientTestGetSimpleIntArray() : Promise<ITsclientTestGetSimpleIntArrayResponse[]> {
    const response = await fetch(baseUrl + "/api/tsclient-test/get-simple-int-array", {
        method: "GET",
        headers: {
            "Content-Type": "application/json"
        },
    });
    return await response.json() as ITsclientTestGetSimpleIntArrayResponse[];
}
""";

        [Fact]
        public void Test_ArrayTypes_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "array_types.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            // Normalize trailing whitespace on lines (TsClient generates "* " for empty comment lines)
            var normalizedContent = NormalizeTrailingWhitespace(content);
            var normalizedExpected = NormalizeTrailingWhitespace(Expected);
            Assert.True(normalizedContent == normalizedExpected, $"ACTUAL:\n{content}\n\nEXPECTED:\n{Expected}");
        }

        private static string NormalizeTrailingWhitespace(string input)
        {
            var lines = input.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = lines[i].TrimEnd();
            }
            // Also trim trailing empty lines
            return string.Join('\n', lines).TrimEnd('\n');
        }
    }
}
