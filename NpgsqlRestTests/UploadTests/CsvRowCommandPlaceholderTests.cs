using System.Net.Http.Headers;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void CsvRowCommandPlaceholderTests()
    {
        script.Append(@"
create table csv_rc_placeholder_table (tag text);

create procedure csv_rc_placeholder_row(_index int, _row text[], _tag text)
language sql as $$ insert into csv_rc_placeholder_table (tag) values (_tag) $$;

create function csv_rc_placeholder_upload(_tag text = null, _meta json = null)
returns text
language sql as $$
    with removed as (delete from csv_rc_placeholder_table returning tag)
    select string_agg(tag, ',') from removed
$$;

comment on function csv_rc_placeholder_upload(text, json) is '
HTTP POST
upload for csv
param _meta is upload metadata
row_command = call csv_rc_placeholder_row($1,$2,''{_tag}'')
';
");
    }
}

/// <summary>
/// Security regression: {placeholders} are never substituted into an upload row_command, which is SQL text. They used
/// to be replaced with the raw request value - SQL injection for any row_command that referenced a parameter.
/// </summary>
[Collection("TestFixture")]
public class CsvRowCommandPlaceholderTests(TestFixture test)
{
    [Fact]
    public async Task Request_value_is_not_inserted_into_row_command_sql()
    {
        using var formData = new MultipartFormDataContent();
        using var byteContent = new ByteArrayContent(Encoding.UTF8.GetBytes("1,a\n"));
        byteContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        formData.Add(byteContent, "file", "rows.csv");

        using var result = await test.Client.PostAsync("/api/csv-rc-placeholder-upload/?tag=from-request", formData);
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        (await result.Content.ReadAsStringAsync()).Should().Be("{_tag}");
    }
}
