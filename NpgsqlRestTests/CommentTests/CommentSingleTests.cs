namespace NpgsqlRestTests;

public static partial class Database
{
    public static void CommentSingleTests()
    {
        script.Append(@"
-- Single record from a function returning SETOF (multiple rows, but @single returns only first as object)
create function comment_single_setof() returns setof text language sql as
'select ''row1'' union all select ''row2'' union all select ''row3''';
comment on function comment_single_setof() is 'HTTP GET
@single';

-- Single record from a function returning a table (multi-column, returns object not array)
create function comment_single_table()
returns table(id int, name text) language sql as
'select 1, ''alice'' union all select 2, ''bob''';
comment on function comment_single_table() is 'HTTP GET
@single';

-- Single record from a function that naturally returns one row (SETOF with one result)
create function comment_single_one_row() returns setof text language sql as
'select ''only_one''';
comment on function comment_single_one_row() is 'HTTP GET
@single';

-- Without @single for comparison (returns array)
create function comment_no_single_setof() returns setof text language sql as
'select ''row1'' union all select ''row2''';
comment on function comment_no_single_setof() is 'HTTP GET';

-- Single with single_record alias
create function comment_single_record_alias() returns setof text language sql as
'select ''alias_result''';
comment on function comment_single_record_alias() is 'HTTP GET
@single_record';

-- Single record from a function returning table with multiple rows - verifies early exit
create function comment_single_multi_row_table()
returns table(id int, val text) language sql as
'select generate_series(1, 100), ''item_'' || generate_series(1, 100)';
comment on function comment_single_multi_row_table() is 'HTTP GET
single';

-- Single record from a function returning table with one column (named, ReturnsUnnamedSet=false)
-- Should return object not bare value
create function comment_single_named_column()
returns table(val text) language sql as
'select ''hello''';
comment on function comment_single_named_column() is 'HTTP GET
@single';

-- Single with empty result, default null handling (empty string)
create function comment_single_empty_default()
returns table(id int, name text) language sql as
'select id, name from (select 1 as id, ''x'' as name) t where false';
comment on function comment_single_empty_default() is 'HTTP GET
@single';

-- Single with empty result, null_literal handling
create function comment_single_empty_null_literal()
returns table(id int, name text) language sql as
'select id, name from (select 1 as id, ''x'' as name) t where false';
comment on function comment_single_empty_null_literal() is 'HTTP GET
@single
response_null null_literal';

-- Single with empty result, no_content handling (204)
create function comment_single_empty_no_content()
returns table(id int, name text) language sql as
'select id, name from (select 1 as id, ''x'' as name) t where false';
comment on function comment_single_empty_no_content() is 'HTTP GET
@single
response_null no_content';

");
    }
}

[Collection("TestFixture")]
public class CommentSingleTests(TestFixture test)
{
    [Fact]
    public async Task Test_single_setof_returns_scalar_not_array()
    {
        using var response = await test.Client.GetAsync("/api/comment-single-setof/");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("\"row1\"");
    }

    [Fact]
    public async Task Test_single_table_returns_object_not_array()
    {
        using var response = await test.Client.GetAsync("/api/comment-single-table/");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("{\"id\":1,\"name\":\"alice\"}");
    }

    [Fact]
    public async Task Test_single_one_row_returns_scalar()
    {
        using var response = await test.Client.GetAsync("/api/comment-single-one-row/");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("\"only_one\"");
    }

    [Fact]
    public async Task Test_no_single_returns_array()
    {
        using var response = await test.Client.GetAsync("/api/comment-no-single-setof/");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[\"row1\",\"row2\"]");
    }

    [Fact]
    public async Task Test_single_record_alias()
    {
        using var response = await test.Client.GetAsync("/api/comment-single-record-alias/");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("\"alias_result\"");
    }

    [Fact]
    public async Task Test_single_multi_row_table_returns_only_first()
    {
        using var response = await test.Client.GetAsync("/api/comment-single-multi-row-table/");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("{\"id\":1,\"val\":\"item_1\"}");
    }

    [Fact]
    public async Task Test_single_named_column_returns_object_not_value()
    {
        using var response = await test.Client.GetAsync("/api/comment-single-named-column/");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("{\"val\":\"hello\"}");
    }

    [Fact]
    public async Task Test_single_empty_default_returns_empty_string()
    {
        using var response = await test.Client.GetAsync("/api/comment-single-empty-default/");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("");
    }

    [Fact]
    public async Task Test_single_empty_null_literal_returns_null()
    {
        using var response = await test.Client.GetAsync("/api/comment-single-empty-null-literal/");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("null");
    }

    [Fact]
    public async Task Test_single_empty_no_content_returns_204()
    {
        using var response = await test.Client.GetAsync("/api/comment-single-empty-no-content/");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
