namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ArrayCompositeTypeTests()
    {
        script.Append(@"
        -- Composite type for array tests
        create type book_item as (
            book_id int,
            title text,
            author_id int
        );

        -- Composite type with boolean and nullable fields
        create type item_details as (
            id int,
            name text,
            active boolean,
            score numeric
        );

        -- Function returning table with array of composite type column
        create function get_authors_with_books()
        returns table(
            author_id int,
            author_name text,
            books book_item[]
        )
        language sql as
        $$
        select * from (values
            (1, 'George Orwell', array[
                row(1, '1984', 1)::book_item,
                row(2, 'Animal Farm', 1)::book_item
            ]),
            (2, 'Jane Austen', array[
                row(3, 'Pride and Prejudice', 2)::book_item,
                row(4, 'Sense and Sensibility', 2)::book_item
            ])
        ) as t(author_id, author_name, books);
        $$;

        -- Function returning single row with array of composite type
        create function get_author_with_books_single(p_author_id int)
        returns table(
            author_id int,
            author_name text,
            books book_item[]
        )
        language sql as
        $$
        select * from (values
            (1, 'George Orwell', array[
                row(1, '1984', 1)::book_item,
                row(2, 'Animal Farm', 1)::book_item
            ])
        ) as t(author_id, author_name, books)
        where author_id = p_author_id;
        $$;

        -- Function with empty array of composite type
        create function get_author_with_empty_books()
        returns table(
            author_id int,
            author_name text,
            books book_item[]
        )
        language sql as
        $$
        select 1, 'New Author', array[]::book_item[];
        $$;

        -- Function with NULL array of composite type
        create function get_author_with_null_books()
        returns table(
            author_id int,
            author_name text,
            books book_item[]
        )
        language sql as
        $$
        select 1, 'New Author', null::book_item[];
        $$;

        -- Function with composite type containing NULLs in array elements
        create function get_items_with_nulls()
        returns table(
            category text,
            items item_details[]
        )
        language sql as
        $$
        select 'Category A', array[
            row(1, 'Item 1', true, 9.5)::item_details,
            row(2, null, false, null)::item_details,
            row(3, 'Item 3', null, 7.2)::item_details
        ];
        $$;

        -- Function with quoted strings in composite array
        create function get_books_with_quotes()
        returns table(
            books book_item[]
        )
        language sql as
        $$
        select array[
            row(1, 'Book with ""quotes""', 1)::book_item,
            row(2, 'Book, with, commas', 1)::book_item,
            row(3, 'Book (with) parens', 1)::book_item
        ];
        $$;

        -- Function with multiple array of composite columns
        create function get_multiple_composite_arrays()
        returns table(
            id int,
            primary_books book_item[],
            secondary_books book_item[]
        )
        language sql as
        $$
        select
            1,
            array[row(1, 'Primary Book 1', 1)::book_item],
            array[row(2, 'Secondary Book 1', 1)::book_item, row(3, 'Secondary Book 2', 1)::book_item];
        $$;

        -- Table type for array tests (arrays of table types, not just composite types)
        create table book_table (
            book_id int primary key,
            title text,
            author_id int
        );

        -- Function returning array of table type
        create function get_authors_with_table_books()
        returns table(
            author_id int,
            author_name text,
            books book_table[]
        )
        language sql as
        $$
        select * from (values
            (1, 'George Orwell', array[
                row(1, '1984', 1)::book_table,
                row(2, 'Animal Farm', 1)::book_table
            ]),
            (2, 'Jane Austen', array[
                row(3, 'Pride and Prejudice', 2)::book_table,
                row(4, 'Sense and Sensibility', 2)::book_table
            ])
        ) as t(author_id, author_name, books);
        $$;
");
    }
}

/// <summary>
/// Tests for arrays of composite types.
/// Arrays of composite types are automatically serialized as JSON arrays of objects.
/// No special annotation is needed - this behavior is automatic.
///
/// PostgreSQL format: {"(1,\"Book Title\",1)","(2,\"Another Book\",1)"}
/// Expected JSON: [{"bookId":1,"title":"Book Title","authorId":1},{"bookId":2,"title":"Another Book","authorId":1}]
/// </summary>
[Collection("TestFixture")]
public class ArrayCompositeTypeTests(TestFixture test)
{
    [Fact]
    public async Task Test_get_authors_with_books_returns_nested_array()
    {
        using var response = await test.Client.GetAsync("/api/get-authors-with-books/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);

        // Expected: array of authors, each with a nested books array of objects
        // Current behavior (without feature): books would be array of strings like ["(1,1984,1)","(2,Animal Farm,1)"]
        // Desired behavior: books should be array of objects
        content.Should().Be(
            "[{\"authorId\":1,\"authorName\":\"George Orwell\",\"books\":[{\"bookId\":1,\"title\":\"1984\",\"authorId\":1},{\"bookId\":2,\"title\":\"Animal Farm\",\"authorId\":1}]}," +
            "{\"authorId\":2,\"authorName\":\"Jane Austen\",\"books\":[{\"bookId\":3,\"title\":\"Pride and Prejudice\",\"authorId\":2},{\"bookId\":4,\"title\":\"Sense and Sensibility\",\"authorId\":2}]}]");
    }

    [Fact]
    public async Task Test_get_author_with_books_single()
    {
        var query = new QueryBuilder
        {
            { "pAuthorId", "1" }
        };
        using var response = await test.Client.GetAsync($"/api/get-author-with-books-single/{query}");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be(
            "[{\"authorId\":1,\"authorName\":\"George Orwell\",\"books\":[{\"bookId\":1,\"title\":\"1984\",\"authorId\":1},{\"bookId\":2,\"title\":\"Animal Farm\",\"authorId\":1}]}]");
    }

    [Fact]
    public async Task Test_get_author_with_empty_books()
    {
        using var response = await test.Client.GetAsync("/api/get-author-with-empty-books/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        // Empty array should serialize as []
        content.Should().Be("[{\"authorId\":1,\"authorName\":\"New Author\",\"books\":[]}]");
    }

    [Fact]
    public async Task Test_get_author_with_null_books()
    {
        using var response = await test.Client.GetAsync("/api/get-author-with-null-books/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        // NULL array should serialize as null
        content.Should().Be("[{\"authorId\":1,\"authorName\":\"New Author\",\"books\":null}]");
    }

    [Fact]
    public async Task Test_get_items_with_nulls()
    {
        using var response = await test.Client.GetAsync("/api/get-items-with-nulls/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        // Array with elements containing null fields
        content.Should().Be(
            "[{\"category\":\"Category A\",\"items\":[{\"id\":1,\"name\":\"Item 1\",\"active\":true,\"score\":9.5}," +
            "{\"id\":2,\"name\":null,\"active\":false,\"score\":null}," +
            "{\"id\":3,\"name\":\"Item 3\",\"active\":null,\"score\":7.2}]}]");
    }

    [Fact]
    public async Task Test_get_books_with_quotes()
    {
        using var response = await test.Client.GetAsync("/api/get-books-with-quotes/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        // Properly escaped quotes, commas, and parentheses in titles
        content.Should().Be(
            "[{\"books\":[{\"bookId\":1,\"title\":\"Book with \\\"quotes\\\"\",\"authorId\":1}," +
            "{\"bookId\":2,\"title\":\"Book, with, commas\",\"authorId\":1}," +
            "{\"bookId\":3,\"title\":\"Book (with) parens\",\"authorId\":1}]}]");
    }

    [Fact]
    public async Task Test_get_multiple_composite_arrays()
    {
        using var response = await test.Client.GetAsync("/api/get-multiple-composite-arrays/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        // Multiple array of composite columns in same row
        content.Should().Be(
            "[{\"id\":1,\"primaryBooks\":[{\"bookId\":1,\"title\":\"Primary Book 1\",\"authorId\":1}]," +
            "\"secondaryBooks\":[{\"bookId\":2,\"title\":\"Secondary Book 1\",\"authorId\":1},{\"bookId\":3,\"title\":\"Secondary Book 2\",\"authorId\":1}]}]");
    }

    [Fact]
    public async Task Test_get_authors_with_table_books()
    {
        // Test array of TABLE type (not composite type) - should work the same
        using var response = await test.Client.GetAsync("/api/get-authors-with-table-books/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be(
            "[{\"authorId\":1,\"authorName\":\"George Orwell\",\"books\":[{\"bookId\":1,\"title\":\"1984\",\"authorId\":1},{\"bookId\":2,\"title\":\"Animal Farm\",\"authorId\":1}]}," +
            "{\"authorId\":2,\"authorName\":\"Jane Austen\",\"books\":[{\"bookId\":3,\"title\":\"Pride and Prejudice\",\"authorId\":2},{\"bookId\":4,\"title\":\"Sense and Sensibility\",\"authorId\":2}]}]");
    }
}
