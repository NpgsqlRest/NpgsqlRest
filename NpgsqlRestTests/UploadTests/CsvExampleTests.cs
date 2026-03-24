using System.Net.Http.Headers;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void CsvExampleTests()
    {
        script.Append(@"
-- table for uploads
create table csv_example_upload_table (
    index int,
    id int,
    name text,
    value int
);

-- row command
create procedure csv_example_upload_table_row(
    _index int,
    _row text[]
)
language plpgsql
as 
$$
begin
    insert into csv_example_upload_table (
        index,
        id, 
        name, 
        value
    ) 
    values (
        _index,
        _row[1]::int,
        _row[2],
        _row[3]::int
    );
end;
$$;

-- HTTP POST endpoint
create function csv_example_upload(
    _meta json = null
)
returns json 
language plpgsql
as 
$$
begin
    -- do something with metadata or raise exception to rollback this upload
    return _meta;
end;
$$;

comment on function csv_example_upload(json) is '
HTTP POST
upload for csv
param _meta is upload metadata
delimiters = ,;
row_command = call csv_example_upload_table_row($1,$2)
';
");
    }
}

[Collection("TestFixture")]
public class CsvExampleTests(TestFixture test)
{
    [Fact]
    public async Task Test_csv_mixed_delimiter_upload()
    {
        var fileName = "test-csv-upload.csv";
        var sb = new StringBuilder();
        sb.AppendLine("11,XXX,333");
        sb.AppendLine("12;YYY;666");
        sb.AppendLine("13;;999");
        sb.AppendLine("14,,,");
        var csvContent = sb.ToString();
        var contentBytes = Encoding.UTF8.GetBytes(csvContent);
        using var formData = new MultipartFormDataContent();
        using var byteContent = new ByteArrayContent(contentBytes);
        byteContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        formData.Add(byteContent, "file", fileName);

        using var result = await test.Client.PostAsync("/api/csv-example-upload/", formData);
        result.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}