#pragma warning disable CS8602 // Dereference of a possibly null reference.
namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ArrayTypeTests()
    {
        script.Append("""""
create function case_return_int_array() 
returns int[]
language plpgsql
as 
$$
begin
    return array[1,2,3];
end;
$$;

create function case_return_text_array() 
returns text[]
language plpgsql
as 
$$
begin
    return array['a', 'bc', 'x,y', 'foo"bar"', '"foo","bar"', 'foo\bar'];
end;
$$;

create function case_return_bool_array() 
returns boolean[]
language plpgsql
as 
$$
begin
    return array[true, false];
end;
$$;

create function case_return_setof_int_array() 
returns setof int[]
language plpgsql
as 
$$
begin
    return query select a from (
        values 
            (array[1,2,3]),
            (array[4,5,6]),
            (array[7,8,9])
    ) t(a);
end;
$$;

create function case_return_setof_bool_array() 
returns setof boolean[]
language plpgsql
as 
$$
begin
    return query select a from (
        values 
            (array[true,false]),
            (array[false,true])
    ) t(a);
end;
$$;

create function case_return_setof_text_array() 
returns setof text[]
language plpgsql
as 
$$
begin
    return query select a from (
        values 
            (array['a','bc']),
            (array['x','yz','foo','bar'])
    ) t(a);
end;
$$;

create function case_return_int_array_with_null() 
returns int[]
language plpgsql
as 
$$
begin
    return array[4,5,6,null];
end;
$$;


create function case_return_array_edge_cases() 
returns text[]
language plpgsql
as 
$$
begin
    return array[
        'foo,bar',
        'foo"bar',
        '"foo"bar"',
        'foo""bar',
        'foo""""bar',
        'foo"",""bar',
        'foo\bar',
        'foo/bar',
        E'foo\nbar'
    ];
end;
$$;

create function case_return_empty_text_array() 
returns text[]
language plpgsql
as 
$$
begin
    return array[]::text[];
end;
$$;

create function case_return_empty_int_array()
returns int[]
language plpgsql
as
$$
begin
    return array[]::int[];
end;
$$;

-- Test arrays with tab characters
create function case_return_array_with_tabs()
returns text[]
language plpgsql
as
$$
begin
    return array[E'col1\tcol2', E'a\tb\tc', E'\t'];
end;
$$;

-- Test arrays with carriage returns
create function case_return_array_with_cr()
returns text[]
language plpgsql
as
$$
begin
    return array[E'line1\rline2', E'\r\n', E'mixed\r\nlines'];
end;
$$;

-- Test arrays with empty strings
create function case_return_array_with_empty_strings()
returns text[]
language plpgsql
as
$$
begin
    return array['', 'non-empty', '', 'another'];
end;
$$;

-- Test arrays with only whitespace
create function case_return_array_with_whitespace()
returns text[]
language plpgsql
as
$$
begin
    return array[' ', '  ', E'\t', E' \t '];
end;
$$;

-- Test arrays with unicode characters
create function case_return_array_with_unicode()
returns text[]
language plpgsql
as
$$
begin
    return array['hello 世界', 'émojis 🎉🚀', '日本語テスト', 'Ω≈ç√∫'];
end;
$$;

-- Test arrays with combined special characters
create function case_return_array_with_combined_special()
returns text[]
language plpgsql
as
$$
begin
    return array[
        E'quotes"and\\backslash',
        E'newline\nand\ttab',
        E'"quoted\nwith\\escape"',
        E'all,special"chars\n\t\\\r'
    ];
end;
$$;

-- Test decimal/numeric arrays
create function case_return_decimal_array()
returns numeric[]
language plpgsql
as
$$
begin
    return array[1.5, 2.75, 3.14159, -99.99, 0.0];
end;
$$;

-- Test timestamp arrays
create function case_return_timestamp_array()
returns timestamp[]
language plpgsql
as
$$
begin
    return array['2024-01-15 10:30:00'::timestamp, '2024-12-31 23:59:59'::timestamp];
end;
$$;

-- Test 2D text array with special characters
create function case_return_2d_text_with_special()
returns text[][]
language plpgsql
as
$$
begin
    return array[['a"b', 'c\d'], [E'e\nf', 'g,h']];
end;
$$;

-- Test array with very long strings
create function case_return_array_with_long_strings()
returns text[]
language plpgsql
as
$$
begin
    return array[
        repeat('a', 1000),
        repeat(E'x"y\\z\n', 100)
    ];
end;
$$;

-- Test array with null-like strings (not actual NULLs)
create function case_return_array_with_null_strings()
returns text[]
language plpgsql
as
$$
begin
    return array['null', 'NULL', 'Null', 'undefined', 'nil'];
end;
$$;

-- Test array with JSON-like strings
create function case_return_array_with_json_strings()
returns text[]
language plpgsql
as
$$
begin
    return array['{"key":"value"}', '[1,2,3]', '{"nested":{"a":1}}'];
end;
$$;
""""");
    }
}

[Collection("TestFixture")]
public class ArrayTypeTests(TestFixture test)
{
    [Fact]
    public async Task Test_case_return_empty_text_array()
    {
        using var result = await test.Client.PostAsync("/api/case-return-empty-text-array/", null);
        var response = await result.Content.ReadAsStringAsync();

        result?.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Content.Headers.ContentType.MediaType.Should().Be("application/json");
        response.Should().Be("[]");
    }

    [Fact]
    public async Task Test_case_return_empty_int_array()
    {
        using var result = await test.Client.PostAsync("/api/case-return-empty-int-array/", null);
        var response = await result.Content.ReadAsStringAsync();

        result?.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Content.Headers.ContentType.MediaType.Should().Be("application/json");
        response.Should().Be("[]");
    }

    [Fact]
    public async Task Test_case_return_int_array()
    {
        using var result = await test.Client.PostAsync("/api/case-return-int-array/", null);
        var response = await result.Content.ReadAsStringAsync();

        result?.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Content.Headers.ContentType.MediaType.Should().Be("application/json");
        response.Should().Be("[1,2,3]");
    }

    [Fact]
    public async Task Test_case_return_text_array()
    {
        using var result = await test.Client.PostAsync("/api/case-return-text-array/", null);
        var response = await result.Content.ReadAsStringAsync();

        result?.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Content.Headers.ContentType.MediaType.Should().Be("application/json");
        response.Should().Be("""["a","bc","x,y","foo\"bar\"","\"foo\",\"bar\"","foo\\bar"]""");

        var array = JsonNode.Parse(response).AsArray();
        array.Count.Should().Be(6);
        array[0].ToJsonString().Should().Be("\"a\"");
        array[1].ToJsonString().Should().Be("\"bc\"");
        array[2].ToJsonString().Should().Be("\"x,y\"");
        array[3].ToJsonString().Should().Be("\"foo\\u0022bar\\u0022\"");
        array[4].ToJsonString().Should().Be("\"\\u0022foo\\u0022,\\u0022bar\\u0022\"");
        array[5].ToJsonString().Should().Be("\"foo\\\\bar\"");
    }

    [Fact]
    public async Task Test_case_return_bool_array()
    {
        using var result = await test.Client.PostAsync("/api/case-return-bool-array/", null);
        var response = await result.Content.ReadAsStringAsync();

        result?.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Content.Headers.ContentType.MediaType.Should().Be("application/json");
        response.Should().Be("[true,false]");
    }

    [Fact]
    public async Task Test_case_return_setof_int_array()
    {
        using var result = await test.Client.PostAsync("/api/case-return-setof-int-array/", null);
        var response = await result.Content.ReadAsStringAsync();

        result?.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Content.Headers.ContentType.MediaType.Should().Be("application/json");
        response.Should().Be("[[1,2,3],[4,5,6],[7,8,9]]");
    }

    [Fact]
    public async Task Test_CaseReturnSetofBoolArray()
    {
        using var result = await test.Client.PostAsync("/api/case-return-setof-bool-array/", null);
        var response = await result.Content.ReadAsStringAsync();

        result?.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Content.Headers.ContentType.MediaType.Should().Be("application/json");
        response.Should().Be("[[true,false],[false,true]]");
    }

    [Fact]
    public async Task Test_case_return_setof_bool_array()
    {
        using var result = await test.Client.PostAsync("/api/case-return-setof-text-array/", null);
        var response = await result.Content.ReadAsStringAsync();

        result?.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Content.Headers.ContentType.MediaType.Should().Be("application/json");
        response.Should().Be("""[["a","bc"],["x","yz","foo","bar"]]""");
    }

    [Fact]
    public async Task Test_case_return_int_array_with_null()
    {
        using var result = await test.Client.PostAsync("/api/case-return-int-array-with-null/", null);
        var response = await result.Content.ReadAsStringAsync();

        result?.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Content.Headers.ContentType.MediaType.Should().Be("application/json");
        response.Should().Be("[4,5,6,null]");
    }

    [Fact]
    public async Task Test_case_return_array_edge_cases()
    {
        using var response = await test.Client.PostAsync("/api/case-return-array-edge-cases", null);
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType.MediaType.Should().Be("application/json");

        var expextedContent = """""
        [
            "foo,bar",
            "foo\"bar",
            "\"foo\"bar\"",
            "foo\"\"bar",
            "foo\"\"\"\"bar",
            "foo\"\",\"\"bar",
            "foo\\bar",
            "foo/bar",
            "foo\nbar"
        ]
        """""
        .Replace(" ", "")
        .Replace("\r", "")
        .Replace("\n", "");

        content.Should().Be(expextedContent);
    }

    /// <summary>
    /// Test arrays with tab characters produce valid JSON.
    /// </summary>
    [Fact]
    public async Task Test_case_return_array_with_tabs_is_valid_json()
    {
        using var response = await test.Client.PostAsync("/api/case-return-array-with-tabs", null);
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);

        // Must be valid JSON
        var action = () => JsonDocument.Parse(content);
        action.Should().NotThrow("Array with tabs should produce valid JSON");

        // Verify tabs are escaped as \t in JSON
        content.Should().Contain("\\t");

        var array = JsonNode.Parse(content)!.AsArray();
        array.Count.Should().Be(3);
        array[0]!.GetValue<string>().Should().Be("col1\tcol2");
        array[1]!.GetValue<string>().Should().Be("a\tb\tc");
        array[2]!.GetValue<string>().Should().Be("\t");
    }

    /// <summary>
    /// Test arrays with carriage returns produce valid JSON.
    /// </summary>
    [Fact]
    public async Task Test_case_return_array_with_cr_is_valid_json()
    {
        using var response = await test.Client.PostAsync("/api/case-return-array-with-cr", null);
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);

        var action = () => JsonDocument.Parse(content);
        action.Should().NotThrow("Array with carriage returns should produce valid JSON");

        // Verify CR is escaped as \r in JSON
        content.Should().Contain("\\r");

        var array = JsonNode.Parse(content)!.AsArray();
        array.Count.Should().Be(3);
        array[0]!.GetValue<string>().Should().Be("line1\rline2");
    }

    /// <summary>
    /// Test arrays with empty strings produce valid JSON.
    /// </summary>
    [Fact]
    public async Task Test_case_return_array_with_empty_strings_is_valid_json()
    {
        using var response = await test.Client.PostAsync("/api/case-return-array-with-empty-strings", null);
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);

        var action = () => JsonDocument.Parse(content);
        action.Should().NotThrow("Array with empty strings should produce valid JSON");

        content.Should().Be("""["","non-empty","","another"]""");

        var array = JsonNode.Parse(content)!.AsArray();
        array.Count.Should().Be(4);
        array[0]!.GetValue<string>().Should().Be("");
        array[2]!.GetValue<string>().Should().Be("");
    }

    /// <summary>
    /// Test arrays with whitespace-only strings produce valid JSON.
    /// </summary>
    [Fact]
    public async Task Test_case_return_array_with_whitespace_is_valid_json()
    {
        using var response = await test.Client.PostAsync("/api/case-return-array-with-whitespace", null);
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);

        var action = () => JsonDocument.Parse(content);
        action.Should().NotThrow("Array with whitespace strings should produce valid JSON");

        var array = JsonNode.Parse(content)!.AsArray();
        array.Count.Should().Be(4);
        array[0]!.GetValue<string>().Should().Be(" ");
        array[1]!.GetValue<string>().Should().Be("  ");
        array[2]!.GetValue<string>().Should().Be("\t");
    }

    /// <summary>
    /// Test arrays with unicode characters produce valid JSON.
    /// </summary>
    [Fact]
    public async Task Test_case_return_array_with_unicode_is_valid_json()
    {
        using var response = await test.Client.PostAsync("/api/case-return-array-with-unicode", null);
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);

        var action = () => JsonDocument.Parse(content);
        action.Should().NotThrow("Array with unicode characters should produce valid JSON");

        var array = JsonNode.Parse(content)!.AsArray();
        array.Count.Should().Be(4);
        array[0]!.GetValue<string>().Should().Be("hello 世界");
        array[1]!.GetValue<string>().Should().Contain("🎉");
        array[2]!.GetValue<string>().Should().Be("日本語テスト");
    }

    /// <summary>
    /// Test arrays with combined special characters produce valid JSON.
    /// </summary>
    [Fact]
    public async Task Test_case_return_array_with_combined_special_is_valid_json()
    {
        using var response = await test.Client.PostAsync("/api/case-return-array-with-combined-special", null);
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);

        var action = () => JsonDocument.Parse(content);
        action.Should().NotThrow("Array with combined special characters should produce valid JSON");

        var array = JsonNode.Parse(content)!.AsArray();
        array.Count.Should().Be(4);
        array[0]!.GetValue<string>().Should().Be("quotes\"and\\backslash");
        array[1]!.GetValue<string>().Should().Be("newline\nand\ttab");
        array[2]!.GetValue<string>().Should().Be("\"quoted\nwith\\escape\"");
        array[3]!.GetValue<string>().Should().Be("all,special\"chars\n\t\\\r");
    }

    /// <summary>
    /// Test decimal/numeric arrays produce valid JSON.
    /// </summary>
    [Fact]
    public async Task Test_case_return_decimal_array_is_valid_json()
    {
        using var response = await test.Client.PostAsync("/api/case-return-decimal-array", null);
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);

        var action = () => JsonDocument.Parse(content);
        action.Should().NotThrow("Decimal array should produce valid JSON");

        content.Should().Be("[1.5,2.75,3.14159,-99.99,0.0]");
    }

    /// <summary>
    /// Test timestamp arrays produce valid JSON.
    /// </summary>
    [Fact]
    public async Task Test_case_return_timestamp_array_is_valid_json()
    {
        using var response = await test.Client.PostAsync("/api/case-return-timestamp-array", null);
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);

        var action = () => JsonDocument.Parse(content);
        action.Should().NotThrow("Timestamp array should produce valid JSON");

        // Timestamps should be ISO format strings
        content.Should().Contain("2024-01-15T10:30:00");
        content.Should().Contain("2024-12-31T23:59:59");
    }

    /// <summary>
    /// Test 2D text array with special characters produces valid JSON.
    /// </summary>
    [Fact]
    public async Task Test_case_return_2d_text_with_special_is_valid_json()
    {
        using var response = await test.Client.PostAsync("/api/case-return-2d-text-with-special", null);
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);

        var action = () => JsonDocument.Parse(content);
        action.Should().NotThrow("2D text array with special characters should produce valid JSON");

        // Should be nested arrays with properly escaped content
        var doc = JsonDocument.Parse(content);
        doc.RootElement.GetArrayLength().Should().Be(2);
    }

    /// <summary>
    /// Test array with very long strings produces valid JSON.
    /// </summary>
    [Fact]
    public async Task Test_case_return_array_with_long_strings_is_valid_json()
    {
        using var response = await test.Client.PostAsync("/api/case-return-array-with-long-strings", null);
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);

        var action = () => JsonDocument.Parse(content);
        action.Should().NotThrow("Array with long strings should produce valid JSON");

        var array = JsonNode.Parse(content)!.AsArray();
        array.Count.Should().Be(2);
        array[0]!.GetValue<string>().Length.Should().Be(1000);
    }

    /// <summary>
    /// Test array with null-like strings (not actual NULLs) produces valid JSON.
    /// These should be string literals, not JSON null values.
    /// </summary>
    [Fact]
    public async Task Test_case_return_array_with_null_strings_is_valid_json()
    {
        using var response = await test.Client.PostAsync("/api/case-return-array-with-null-strings", null);
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);

        var action = () => JsonDocument.Parse(content);
        action.Should().NotThrow("Array with null-like strings should produce valid JSON");

        // These should be quoted strings, not JSON null
        content.Should().Be("""["null","NULL","Null","undefined","nil"]""");

        var array = JsonNode.Parse(content)!.AsArray();
        array.Count.Should().Be(5);
        // Verify they are strings, not null
        foreach (var item in array)
        {
            item.Should().NotBeNull();
            item!.GetValueKind().Should().Be(JsonValueKind.String);
        }
    }

    /// <summary>
    /// Test array with JSON-like strings produces valid JSON.
    /// The inner JSON should be escaped as string content.
    /// </summary>
    [Fact]
    public async Task Test_case_return_array_with_json_strings_is_valid_json()
    {
        using var response = await test.Client.PostAsync("/api/case-return-array-with-json-strings", null);
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);

        var action = () => JsonDocument.Parse(content);
        action.Should().NotThrow("Array with JSON-like strings should produce valid JSON");

        var array = JsonNode.Parse(content)!.AsArray();
        array.Count.Should().Be(3);

        // These should be strings containing JSON, not parsed JSON objects
        array[0]!.GetValueKind().Should().Be(JsonValueKind.String);
        array[0]!.GetValue<string>().Should().Be("{\"key\":\"value\"}");

        array[1]!.GetValueKind().Should().Be(JsonValueKind.String);
        array[1]!.GetValue<string>().Should().Be("[1,2,3]");
    }
}
