namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void TsClientDeepNestedCompositeTypeTests()
        {
            script.Append("""
create schema if not exists tsclient_test;

-- Level 1: Innermost type (reviews)
create type tsclient_test.nested_review as (
    review_id int,
    book_id int,
    reviewer_name text,
    rating int,
    review_text text
);

-- Level 2: Type containing array of level 1 (book with reviews)
create type tsclient_test.nested_book_details as (
    book_id int,
    title text,
    reviews tsclient_test.nested_review[]
);

-- Level 3: Type containing nested level 2 (author info with single book)
create type tsclient_test.nested_author_info as (
    author_id int,
    first_name text,
    last_name text
);

-- Function returning deep nested structure: author + array of book_details (which contains array of reviews)
create or replace function tsclient_test.get_authors_with_books_and_reviews(
    _author_id int default null
)
returns table(
    author tsclient_test.nested_author_info,
    books tsclient_test.nested_book_details[]
)
language sql as
$$
select
    row(1, 'George', 'Orwell')::tsclient_test.nested_author_info,
    array[
        row(1, '1984', array[
            row(1, 1, 'Alice', 5, 'Great book!')::tsclient_test.nested_review,
            row(2, 1, 'Bob', 4, 'Good read')::tsclient_test.nested_review
        ])::tsclient_test.nested_book_details,
        row(2, 'Animal Farm', array[
            row(3, 2, 'Charlie', 5, 'Brilliant!')::tsclient_test.nested_review
        ])::tsclient_test.nested_book_details
    ];
$$;
comment on function tsclient_test.get_authors_with_books_and_reviews(int) is '
tsclient_module=deep_nested_composite
nested
';

-- 4-level deep nesting: level1 -> level2 -> level3 -> level4
create type tsclient_test.deep_level1 as (id int, value text);
create type tsclient_test.deep_level2 as (name text, inner1 tsclient_test.deep_level1);
create type tsclient_test.deep_level3 as (label text, inner2 tsclient_test.deep_level2);
create type tsclient_test.deep_level4 as (tag text, inner3 tsclient_test.deep_level3);

create or replace function tsclient_test.get_4_level_nested()
returns table(data tsclient_test.deep_level4[])
language sql as
$$
select array[
    row('top',
        row('level3',
            row('level2',
                row(1, 'bottom')::tsclient_test.deep_level1
            )::tsclient_test.deep_level2
        )::tsclient_test.deep_level3
    )::tsclient_test.deep_level4
];
$$;
comment on function tsclient_test.get_4_level_nested() is '
tsclient_module=deep_nested_composite
nested
';

-- Type with nested composite containing array of composites
create type tsclient_test.inner_member as (member_id int, name text);
create type tsclient_test.group_with_members as (group_name text, members tsclient_test.inner_member[]);
create type tsclient_test.outer_container as (id int, nested_group tsclient_test.group_with_members);

create or replace function tsclient_test.get_nested_with_array_inside()
returns table(data tsclient_test.outer_container[])
language sql as
$$
select array[
    row(1,
        row('Group A', array[
            row(10, 'Alice')::tsclient_test.inner_member,
            row(20, 'Bob')::tsclient_test.inner_member
        ])::tsclient_test.group_with_members
    )::tsclient_test.outer_container
];
$$;
comment on function tsclient_test.get_nested_with_array_inside() is '
tsclient_module=deep_nested_composite
nested
';
""");
        }
    }
}

namespace NpgsqlRestTests.TsClientTests
{
    [Collection("TestFixture")]
    public class DeepNestedCompositeTypeTests
    {
        // Expected TypeScript output for deep nested composite types
        // The key test cases:
        // 1. Array of composites where each composite contains another array of composites (books -> reviews)
        // 2. 4-level deep nesting (level4 -> level3 -> level2 -> level1)
        // 3. Composite containing nested composite that contains array of composites
        private const string Expected = """
const baseUrl = "";
const parseQuery = (query: Record<any, any>) => "?" + Object.keys(query ? query : {})
    .map(key => {
        const value = (query[key] != null ? query[key] : "") as string;
        if (Array.isArray(value)) {
            return value.map((s: string) => s ? `${key}=${encodeURIComponent(s)}` : `${key}=`).join("&");
        }
        return `${key}=${encodeURIComponent(value)}`;
    })
    .join("&");

interface IInner1 {
    id: number | null;
    value: string | null;
}

interface IInner2 {
    name: string | null;
    inner1: IInner1 | null;
}

interface IInner3 {
    label: string | null;
    inner2: IInner2 | null;
}

interface IData {
    tag: string | null;
    inner3: IInner3 | null;
}

interface IAuthor {
    authorId: number | null;
    firstName: string | null;
    lastName: string | null;
}

interface IReviews {
    reviewId: number | null;
    bookId: number | null;
    reviewerName: string | null;
    rating: number | null;
    reviewText: string | null;
}

interface IBooks {
    bookId: number | null;
    title: string | null;
    reviews: IReviews[] | null;
}

interface IMembers {
    memberId: number | null;
    name: string | null;
}

interface INestedGroup {
    groupName: string | null;
    members: IMembers[] | null;
}

interface IData1 {
    id: number | null;
    nestedGroup: INestedGroup | null;
}

interface ITsclientTestGet4LevelNestedResponse {
    data: IData[] | null;
}

interface ITsclientTestGetAuthorsWithBooksAndReviewsRequest {
    authorId?: number | null;
}

interface ITsclientTestGetAuthorsWithBooksAndReviewsResponse {
    author: IAuthor | null;
    books: IBooks[] | null;
}

interface ITsclientTestGetNestedWithArrayInsideResponse {
    data: IData1[] | null;
}


/**
* function tsclient_test.get_4_level_nested()
* returns table(
*     data tsclient_test.deep_level4[]
* )
*
* @remarks
* comment on function tsclient_test.get_4_level_nested is 'tsclient_module=deep_nested_composite
* nested';
*
* @returns {ITsclientTestGet4LevelNestedResponse[]}
*
* @see FUNCTION tsclient_test.get_4_level_nested
*/
export async function tsclientTestGet4LevelNested() : Promise<ITsclientTestGet4LevelNestedResponse[]> {
    const response = await fetch(baseUrl + "/api/tsclient-test/get-4-level-nested", {
        method: "GET",
        headers: {
            "Content-Type": "application/json"
        },
    });
    return await response.json() as ITsclientTestGet4LevelNestedResponse[];
}

/**
* function tsclient_test.get_authors_with_books_and_reviews(
*     _author_id integer DEFAULT NULL::integer
* )
* returns table(
*     author integer,
*     author text,
*     author text,
*     books tsclient_test.nested_book_details[]
* )
*
* @remarks
* comment on function tsclient_test.get_authors_with_books_and_reviews is 'tsclient_module=deep_nested_composite
* nested';
*
* @param request - Object containing request parameters.
* @returns {ITsclientTestGetAuthorsWithBooksAndReviewsResponse[]}
*
* @see FUNCTION tsclient_test.get_authors_with_books_and_reviews
*/
export async function tsclientTestGetAuthorsWithBooksAndReviews(
    request: ITsclientTestGetAuthorsWithBooksAndReviewsRequest
) : Promise<ITsclientTestGetAuthorsWithBooksAndReviewsResponse[]> {
    const response = await fetch(baseUrl + "/api/tsclient-test/get-authors-with-books-and-reviews" + parseQuery(request), {
        method: "GET",
        headers: {
            "Content-Type": "application/json"
        },
    });
    return await response.json() as ITsclientTestGetAuthorsWithBooksAndReviewsResponse[];
}

/**
* function tsclient_test.get_nested_with_array_inside()
* returns table(
*     data tsclient_test.outer_container[]
* )
*
* @remarks
* comment on function tsclient_test.get_nested_with_array_inside is 'tsclient_module=deep_nested_composite
* nested';
*
* @returns {ITsclientTestGetNestedWithArrayInsideResponse[]}
*
* @see FUNCTION tsclient_test.get_nested_with_array_inside
*/
export async function tsclientTestGetNestedWithArrayInside() : Promise<ITsclientTestGetNestedWithArrayInsideResponse[]> {
    const response = await fetch(baseUrl + "/api/tsclient-test/get-nested-with-array-inside", {
        method: "GET",
        headers: {
            "Content-Type": "application/json"
        },
    });
    return await response.json() as ITsclientTestGetNestedWithArrayInsideResponse[];
}
""";

        [Fact]
        public void Test_DeepNestedCompositeTypes_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "deep_nested_composite.ts");
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
