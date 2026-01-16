namespace NpgsqlRestTests;

public static partial class Database
{
    public static void TupleStringEscapingTests()
    {
        script.Append(@"
        -- Tests for proper JSON escaping of PostgreSQL tuple strings
        -- When composite types are serialized as tuple strings, special characters must be escaped

        -- Type with text field that can contain special characters
        create type ts_inner_type as (
            id int,
            value text
        );

        -- Outer type containing inner composite (will be serialized as tuple string)
        create type ts_outer_type as (
            label text,
            nested ts_inner_type
        );

        -- Type with array of composites (elements will be tuple strings)
        create type ts_with_array_type as (
            name text,
            items ts_inner_type[]
        );

        -- 1. Test nested composite with quotes in value
        create function get_tuple_with_quotes()
        returns table(data ts_outer_type[])
        language sql as
        $$
        select array[
            row('test', row(1, 'hello ""world""')::ts_inner_type)::ts_outer_type
        ];
        $$;

        -- 2. Test nested composite with backslash in value
        create function get_tuple_with_backslash()
        returns table(data ts_outer_type[])
        language sql as
        $$
        select array[
            row('test', row(1, 'path\to\file')::ts_inner_type)::ts_outer_type
        ];
        $$;

        -- 3. Test nested composite with newline in value
        create function get_tuple_with_newline()
        returns table(data ts_outer_type[])
        language sql as
        $$
        select array[
            row('test', row(1, E'line1\nline2')::ts_inner_type)::ts_outer_type
        ];
        $$;

        -- 4. Test nested composite with tab in value
        create function get_tuple_with_tab()
        returns table(data ts_outer_type[])
        language sql as
        $$
        select array[
            row('test', row(1, E'col1\tcol2')::ts_inner_type)::ts_outer_type
        ];
        $$;

        -- 5. Test nested composite with parentheses in value (tuple-like)
        create function get_tuple_with_parens()
        returns table(data ts_outer_type[])
        language sql as
        $$
        select array[
            row('test', row(1, '(nested,value)')::ts_inner_type)::ts_outer_type
        ];
        $$;

        -- 6. Test nested composite with comma in value
        create function get_tuple_with_comma()
        returns table(data ts_outer_type[])
        language sql as
        $$
        select array[
            row('test', row(1, 'a,b,c')::ts_inner_type)::ts_outer_type
        ];
        $$;

        -- 7. Test array of composites with special chars (elements become tuple strings)
        create function get_array_with_special_chars()
        returns table(data ts_with_array_type[])
        language sql as
        $$
        select array[
            row('group1', array[
                row(1, 'hello ""quoted""')::ts_inner_type,
                row(2, E'with\nnewline')::ts_inner_type
            ])::ts_with_array_type
        ];
        $$;

        -- 8. Test 2D composite array with special chars
        create function get_2d_tuple_with_special_chars()
        returns table(matrix ts_inner_type[][])
        language sql as
        $$
        select array[
            [row(1, 'normal')::ts_inner_type, row(2, 'with ""quotes""')::ts_inner_type],
            [row(3, E'with\nnewline')::ts_inner_type, row(4, 'with,comma')::ts_inner_type]
        ];
        $$;
");
    }
}

/// <summary>
/// Tests to verify that PostgreSQL tuple strings are properly JSON-escaped.
/// When composite types are serialized as tuple strings (e.g., "(1,value)"),
/// any special characters in the values must be properly escaped in the JSON output.
/// </summary>
[Collection("TestFixture")]
public class TupleStringEscapingTests(TestFixture test)
{
    /// <summary>
    /// Test that quotes inside tuple strings are properly escaped.
    /// PostgreSQL tuple: (1,"hello ""world""")
    /// JSON must escape the quotes properly.
    /// </summary>
    [Fact]
    public async Task Test_tuple_with_quotes_is_valid_json()
    {
        using var response = await test.Client.GetAsync("/api/get-tuple-with-quotes/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);

        // The JSON must be valid - try to parse it
        var doc = System.Text.Json.JsonDocument.Parse(content);

        // Verify the nested field decodes correctly
        // PostgreSQL SQL input: 'hello ""world""' - this is the literal string hello ""world"" (with two quotes)
        // In tuple format: (1,"hello """"world""""") - text field is quoted, each " becomes ""
        // Expected decoded tuple string: (1,"hello ""world""")
        var nested = doc.RootElement[0].GetProperty("data")[0].GetProperty("nested").GetString();
        nested.Should().Be("(1,\"hello \"\"world\"\"\")",
            $"the decoded tuple string should have proper tuple format with doubled quotes. Raw JSON: {content}");
    }

    /// <summary>
    /// Test that backslashes inside tuple strings are properly escaped.
    /// </summary>
    [Fact]
    public async Task Test_tuple_with_backslash_is_valid_json()
    {
        using var response = await test.Client.GetAsync("/api/get-tuple-with-backslash/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);

        var action = () => System.Text.Json.JsonDocument.Parse(content);
        action.Should().NotThrow("JSON with backslash in tuple string should be valid");
    }

    /// <summary>
    /// Test that newlines inside tuple strings are properly escaped.
    /// </summary>
    [Fact]
    public async Task Test_tuple_with_newline_is_valid_json()
    {
        using var response = await test.Client.GetAsync("/api/get-tuple-with-newline/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);

        var action = () => System.Text.Json.JsonDocument.Parse(content);
        action.Should().NotThrow("JSON with newline in tuple string should be valid");

        // Newlines should be escaped as \n in JSON
        content.Should().Contain("\\n");
    }

    /// <summary>
    /// Test that tabs inside tuple strings are properly escaped.
    /// </summary>
    [Fact]
    public async Task Test_tuple_with_tab_is_valid_json()
    {
        using var response = await test.Client.GetAsync("/api/get-tuple-with-tab/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);

        var action = () => System.Text.Json.JsonDocument.Parse(content);
        action.Should().NotThrow("JSON with tab in tuple string should be valid");

        // Tabs should be escaped as \t in JSON
        content.Should().Contain("\\t");
    }

    /// <summary>
    /// Test that parentheses inside tuple strings don't break parsing.
    /// </summary>
    [Fact]
    public async Task Test_tuple_with_parens_is_valid_json()
    {
        using var response = await test.Client.GetAsync("/api/get-tuple-with-parens/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);

        var action = () => System.Text.Json.JsonDocument.Parse(content);
        action.Should().NotThrow("JSON with parentheses in tuple string should be valid");
    }

    /// <summary>
    /// Test that commas inside tuple strings don't break parsing.
    /// </summary>
    [Fact]
    public async Task Test_tuple_with_comma_is_valid_json()
    {
        using var response = await test.Client.GetAsync("/api/get-tuple-with-comma/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);

        var action = () => System.Text.Json.JsonDocument.Parse(content);
        action.Should().NotThrow("JSON with comma in tuple string should be valid");
    }

    /// <summary>
    /// Test array of composites with special characters (elements become tuple strings).
    /// </summary>
    [Fact]
    public async Task Test_array_of_tuples_with_special_chars_is_valid_json()
    {
        using var response = await test.Client.GetAsync("/api/get-array-with-special-chars/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = System.Text.Json.JsonDocument.Parse(content);

        // The items field should be an array of tuple strings
        // Each element like (1,"hello ""quoted""") when decoded from JSON should have:
        // - Proper tuple format with quotes for the text field
        // - Doubled quotes "" representing literal " in the value
        var items = doc.RootElement[0].GetProperty("data")[0].GetProperty("items");
        items.GetArrayLength().Should().Be(2);

        var firstItem = items[0].GetString();
        // Original value: hello "quoted"
        // In tuple: (1,"hello ""quoted""")
        // The decoded string should NOT have backslash escaping like \"
        firstItem.Should().NotBeNull();
        firstItem.Should().NotContain("\\\"", "decoded tuple string should not contain backslash-escaped quotes");
        // It should have the proper tuple format with doubled quotes for literal "
        firstItem.Should().Be("(1,\"hello \"\"quoted\"\"\")");
    }

    /// <summary>
    /// Test 2D composite array with special characters.
    /// </summary>
    [Fact]
    public async Task Test_2d_tuple_array_with_special_chars_is_valid_json()
    {
        using var response = await test.Client.GetAsync("/api/get-2d-tuple-with-special-chars/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);

        var action = () => System.Text.Json.JsonDocument.Parse(content);
        action.Should().NotThrow("JSON with special chars in 2D tuple array should be valid");
    }
}
