namespace NpgsqlRestTests;

public static partial class Database
{
    public static void HtmlTableFormatTests()
    {
        script.Append(@"
create function get_html_table_users()
returns table (id int, name text, email text)
language sql as $$
select * from (values
    (1, 'Alice', 'alice@example.com'),
    (2, 'Bob', 'bob@example.com'),
    (3, 'Charlie', 'charlie@example.com')
) as t(id, name, email);
$$;
comment on function get_html_table_users() is '
HTTP GET
@table_format = html
';

create function get_html_table_special_chars()
returns table (id int, content text)
language sql as $$
select * from (values
    (1, '<script>alert(1)</script>'),
    (2, 'A & B'),
    (3, 'say ""hello""')
) as t(id, content);
$$;
comment on function get_html_table_special_chars() is '
HTTP GET
@table_format = html
';

create function get_html_table_nulls()
returns table (id int, value text)
language sql as $$
select * from (values
    (1, 'has value'),
    (2, null::text),
    (3, 'another value')
) as t(id, value);
$$;
comment on function get_html_table_nulls() is '
HTTP GET
@table_format = html
';

create function get_html_table_empty()
returns table (id int, name text)
language sql as $$
select id, name from (values (1, 'x')) as t(id, name) where false;
$$;
comment on function get_html_table_empty() is '
HTTP GET
@table_format = html
';

create function get_html_table_single_column()
returns setof text
language sql as $$
select * from (values ('row1'), ('row2'), ('row3')) as t(val);
$$;
comment on function get_html_table_single_column() is '
HTTP GET
@table_format = html
';

create function get_no_table_format()
returns table (id int, name text)
language sql as $$
select * from (values (1, 'test')) as t(id, name);
$$;
comment on function get_no_table_format() is '
HTTP GET
';

create function get_html_table_with_param(_filter text)
returns table (id int, name text)
language sql as $$
select * from (values
    (1, 'Alice'),
    (2, 'Bob')
) as t(id, name) where name ilike '%' || _filter || '%';
$$;
comment on function get_html_table_with_param(text) is '
HTTP GET
@table_format = html
';

create function get_dynamic_table_format(_format text)
returns table (id int, name text)
language sql as $$
select * from (values (1, 'Alice'), (2, 'Bob')) as t(id, name);
$$;
comment on function get_dynamic_table_format(text) is '
HTTP GET
@table_format = {_format}
';
");
    }
}

[Collection("TestFixture")]
public class HtmlTableFormatTests(TestFixture test)
{
    [Fact]
    public async Task Test_html_table_basic()
    {
        using var result = await test.Client.GetAsync("/api/get-html-table-users/");
        var response = await result.Content.ReadAsStringAsync();

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
        result.Content.Headers.ContentType?.CharSet.Should().Be("utf-8");

        // Should contain style block
        response.Should().Contain("<style>");
        response.Should().Contain("border-collapse:collapse");

        // Should contain table structure
        response.Should().Contain("<table>");
        response.Should().Contain("</table>");

        // Should contain header row with column names
        response.Should().Contain("<th>id</th>");
        response.Should().Contain("<th>name</th>");
        response.Should().Contain("<th>email</th>");

        // Should contain data rows
        response.Should().Contain("<td>1</td>");
        response.Should().Contain("<td>Alice</td>");
        response.Should().Contain("<td>alice@example.com</td>");
        response.Should().Contain("<td>3</td>");
        response.Should().Contain("<td>Charlie</td>");
    }

    [Fact]
    public async Task Test_html_table_special_characters_are_encoded()
    {
        using var result = await test.Client.GetAsync("/api/get-html-table-special-chars/");
        var response = await result.Content.ReadAsStringAsync();

        result.StatusCode.Should().Be(HttpStatusCode.OK);

        // < and > should be encoded
        response.Should().Contain("&lt;script&gt;alert(1)&lt;/script&gt;");
        response.Should().NotContain("<script>");

        // & should be encoded
        response.Should().Contain("A &amp; B");

        // " should be encoded
        response.Should().Contain("say &quot;hello&quot;");
    }

    [Fact]
    public async Task Test_html_table_null_values_render_empty_cells()
    {
        using var result = await test.Client.GetAsync("/api/get-html-table-nulls/");
        var response = await result.Content.ReadAsStringAsync();

        result.StatusCode.Should().Be(HttpStatusCode.OK);

        // Row with value
        response.Should().Contain("<td>has value</td>");

        // NULL row should have empty td (no content between tags)
        response.Should().Contain("<td>2</td><td></td>");
    }

    [Fact]
    public async Task Test_html_table_empty_result_set()
    {
        using var result = await test.Client.GetAsync("/api/get-html-table-empty/");
        var response = await result.Content.ReadAsStringAsync();

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Content.Headers.ContentType?.MediaType.Should().Be("text/html");

        // Should still have table structure with headers
        response.Should().Contain("<table>");
        response.Should().Contain("<th>id</th>");
        response.Should().Contain("<th>name</th>");
        response.Should().Contain("</table>");

        // Should not contain any data rows
        response.Should().NotContain("<td>");
    }

    [Fact]
    public async Task Test_html_table_single_column_set()
    {
        using var result = await test.Client.GetAsync("/api/get-html-table-single-column/");
        var response = await result.Content.ReadAsStringAsync();

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Content.Headers.ContentType?.MediaType.Should().Be("text/html");

        response.Should().Contain("<td>row1</td>");
        response.Should().Contain("<td>row2</td>");
        response.Should().Contain("<td>row3</td>");
    }

    [Fact]
    public async Task Test_no_table_format_returns_json()
    {
        using var result = await test.Client.GetAsync("/api/get-no-table-format/");
        var response = await result.Content.ReadAsStringAsync();

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        // Should be JSON, not HTML
        response.Should().NotContain("<table>");
        response.Should().Contain("[");
    }

    [Fact]
    public async Task Test_html_table_with_query_parameter()
    {
        using var result = await test.Client.GetAsync("/api/get-html-table-with-param/?filter=Ali");
        var response = await result.Content.ReadAsStringAsync();

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Content.Headers.ContentType?.MediaType.Should().Be("text/html");

        response.Should().Contain("<td>Alice</td>");
        response.Should().NotContain("<td>Bob</td>");
    }

    [Fact]
    public async Task Test_dynamic_table_format_placeholder_resolves_per_request()
    {
        // First call with html format
        using var htmlResult = await test.Client.GetAsync("/api/get-dynamic-table-format/?format=html");
        htmlResult.StatusCode.Should().Be(HttpStatusCode.OK);
        htmlResult.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
        var htmlResponse = await htmlResult.Content.ReadAsStringAsync();
        htmlResponse.Should().Contain("<table>");

        // Second call with excel format — must NOT return html (the bug was that the first
        // request's resolved value permanently overwrote the {_format} template)
        using var excelResult = await test.Client.GetAsync("/api/get-dynamic-table-format/?format=excel");
        excelResult.StatusCode.Should().Be(HttpStatusCode.OK);
        excelResult.Content.Headers.ContentType?.MediaType
            .Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        // Third call with html again — must still work
        using var htmlResult2 = await test.Client.GetAsync("/api/get-dynamic-table-format/?format=html");
        htmlResult2.StatusCode.Should().Be(HttpStatusCode.OK);
        htmlResult2.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
    }
}
