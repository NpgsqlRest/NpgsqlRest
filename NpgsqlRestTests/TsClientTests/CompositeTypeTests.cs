namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void TsClientCompositeTypeTests()
        {
            script.Append("""
create schema if not exists tsclient_test;

-- Composite type for nested JSON tests
create type tsclient_test.nested_address as (
    street text,
    city text,
    zip_code text
);

-- Composite type with various field types
create type tsclient_test.person_info as (
    id int,
    name text,
    age int,
    is_active boolean,
    score numeric
);

-- Function returning table with composite type column (nested JSON)
create function tsclient_test.get_users_with_address()
returns table(
    user_id int,
    user_name text,
    address tsclient_test.nested_address
)
language sql as
$$
select * from (values
    (1, 'Alice', row('123 Main St', 'New York', '10001')::tsclient_test.nested_address),
    (2, 'Bob', row('456 Oak Ave', 'Boston', '02101')::tsclient_test.nested_address)
) as t(user_id, user_name, address);
$$;
comment on function tsclient_test.get_users_with_address() is '
tsclient_module=composite_types
nested
';

-- Function returning array of composite type
create function tsclient_test.get_authors_with_books()
returns table(
    author_id int,
    author_name text,
    books tsclient_test.person_info[]
)
language sql as
$$
select * from (values
    (1, 'Author One', array[
        row(1, 'Book 1', 100, true, 9.5)::tsclient_test.person_info,
        row(2, 'Book 2', 200, false, 8.0)::tsclient_test.person_info
    ])
) as t(author_id, author_name, books);
$$;
comment on function tsclient_test.get_authors_with_books() is '
tsclient_module=composite_types
';

-- Function with both: composite column AND array of composite
create function tsclient_test.get_complex_data()
returns table(
    id int,
    main_address tsclient_test.nested_address,
    contacts tsclient_test.person_info[]
)
language sql as
$$
select
    1,
    row('789 Pine Rd', 'Chicago', '60601')::tsclient_test.nested_address,
    array[
        row(1, 'Contact 1', 30, true, 7.5)::tsclient_test.person_info,
        row(2, 'Contact 2', 25, false, 8.5)::tsclient_test.person_info
    ];
$$;
comment on function tsclient_test.get_complex_data() is '
tsclient_module=composite_types
nested
';
""");
        }
    }
}

namespace NpgsqlRestTests.TsClientTests
{
    [Collection("TestFixture")]
    public class CompositeTypeTests
    {
        // Expected TypeScript output for composite types
        // The nested composite type should generate a separate interface
        // Arrays of composite types should be typed as InterfaceName[]
        private const string Expected = """
const baseUrl = "";

interface IBooks {
    id: number | null;
    name: string | null;
    age: number | null;
    isActive: boolean | null;
    score: number | null;
}

interface IMainAddress {
    street: string | null;
    city: string | null;
    zipCode: string | null;
}

interface ITsclientTestGetAuthorsWithBooksResponse {
    authorId: number | null;
    authorName: string | null;
    books: IBooks[] | null;
}

interface ITsclientTestGetComplexDataResponse {
    id: number | null;
    mainAddress: IMainAddress | null;
    contacts: IBooks[] | null;
}

interface ITsclientTestGetUsersWithAddressResponse {
    userId: number | null;
    userName: string | null;
    address: IMainAddress | null;
}


/**
* function tsclient_test.get_authors_with_books()
* returns table(
*     author_id integer,
*     author_name text,
*     books tsclient_test.person_info[]
* )
*
* @remarks
* comment on function tsclient_test.get_authors_with_books is 'tsclient_module=composite_types
*
* @returns {ITsclientTestGetAuthorsWithBooksResponse[]}
*
* @see FUNCTION tsclient_test.get_authors_with_books
*/
export async function tsclientTestGetAuthorsWithBooks() : Promise<ITsclientTestGetAuthorsWithBooksResponse[]> {
    const response = await fetch(baseUrl + "/api/tsclient-test/get-authors-with-books", {
        method: "GET",
        headers: {
            "Content-Type": "application/json"
        },
    });
    return await response.json() as ITsclientTestGetAuthorsWithBooksResponse[];
}

/**
* function tsclient_test.get_complex_data()
* returns table(
*     id integer,
*     main_address text,
*     main_address text,
*     main_address text,
*     contacts tsclient_test.person_info[]
* )
*
* @remarks
* comment on function tsclient_test.get_complex_data is 'tsclient_module=composite_types
* nested';
*
* @returns {ITsclientTestGetComplexDataResponse[]}
*
* @see FUNCTION tsclient_test.get_complex_data
*/
export async function tsclientTestGetComplexData() : Promise<ITsclientTestGetComplexDataResponse[]> {
    const response = await fetch(baseUrl + "/api/tsclient-test/get-complex-data", {
        method: "GET",
        headers: {
            "Content-Type": "application/json"
        },
    });
    return await response.json() as ITsclientTestGetComplexDataResponse[];
}

/**
* function tsclient_test.get_users_with_address()
* returns table(
*     user_id integer,
*     user_name text,
*     address text,
*     address text,
*     address text
* )
*
* @remarks
* comment on function tsclient_test.get_users_with_address is 'tsclient_module=composite_types
* nested';
*
* @returns {ITsclientTestGetUsersWithAddressResponse[]}
*
* @see FUNCTION tsclient_test.get_users_with_address
*/
export async function tsclientTestGetUsersWithAddress() : Promise<ITsclientTestGetUsersWithAddressResponse[]> {
    const response = await fetch(baseUrl + "/api/tsclient-test/get-users-with-address", {
        method: "GET",
        headers: {
            "Content-Type": "application/json"
        },
    });
    return await response.json() as ITsclientTestGetUsersWithAddressResponse[];
}
""";

        [Fact]
        public void Test_CompositeTypes_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "composite_types.ts");
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
