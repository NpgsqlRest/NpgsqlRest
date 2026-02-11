using System.Net.Http.Headers;
using System.Text.Json;
using Npgsql;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void CsvUploadTests()
    {
        script.Append(@"
        create table csv_simple_upload_table
        (
            index int,
            id int,
            name text,
            value int,
            prev_result int,
            meta json
        );

        create function csv_simple_upload_process_row(
            _index int,
            _row text[],
            _prev_result int,
            _meta json
        )
        returns int
        language plpgsql
        as 
        $$
        begin
            if _index > 1 then
                insert into csv_simple_upload_table (
                    index,
                    id, 
                    name, 
                    value, 
                    prev_result, 
                    meta
                ) 
                values (
                    _index,
                    _row[1]::int,
                    _row[2],
                    _row[3]::int,
                    _prev_result,
                    _meta
                );
            end if;
            return _index;
        end;
        $$;

        create function csv_simple_upload(
            _meta json = null
        )
        returns json 
        language plpgsql
        as 
        $$
        begin
            return _meta;
        end;
        $$;

        comment on function csv_simple_upload(json) is '
        upload for csv
        param _meta is upload metadata
        row_command = select csv_simple_upload_process_row($1,$2,$3,$4)
        ';

        create table csv_mixed_delimiter_upload_table
        (
            index int,
            id int,
            name text,
            value int
        );
        create procedure csv_mixed_delimiter_upload_row(
            _index int,
            _row text[]
        )
        language plpgsql
        as 
        $$
        begin
            insert into csv_mixed_delimiter_upload_table (
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

        -- will use tab, comma and semicolon as delimiters
        create function csv_mixed_delimiter_upload(
            _meta json = null
        )
        returns json 
        language plpgsql
        as 
        $$
        begin
            return _meta;
        end;
        $$;

        comment on function csv_mixed_delimiter_upload(json) is '
        upload for csv
        param _meta is upload metadata
        delimiters = \t,;
        row_command = call csv_mixed_delimiter_upload_row($1,$2)
        ';

        -- CSV fallback to large_object test
        create table csv_fallback_lo_upload_table
        (
            index int,
            id int,
            name text,
            value int
        );

        create function csv_fallback_lo_upload_process_row(
            _index int,
            _row text[]
        )
        returns int
        language plpgsql
        as
        $$
        begin
            if _index > 1 then
                insert into csv_fallback_lo_upload_table (
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
            end if;
            return _index;
        end;
        $$;

        create function csv_fallback_lo_upload(
            _meta json = null
        )
        returns json
        language plpgsql
        as
        $$
        begin
            return _meta;
        end;
        $$;

        comment on function csv_fallback_lo_upload(json) is '
        upload for csv
        param _meta is upload metadata
        check_format = true
        fallback_handler = large_object
        row_command = select csv_fallback_lo_upload_process_row($1,$2)
        ';

        -- CSV no fallback test (check_format = true, no fallback_handler)
        create function csv_no_fallback_upload(
            _meta json = null
        )
        returns json
        language plpgsql
        as
        $$
        begin
            return _meta;
        end;
        $$;

        comment on function csv_no_fallback_upload(json) is '
        upload for csv
        param _meta is upload metadata
        check_format = true
        row_command = call csv_mixed_delimiter_upload_row($1,$2)
        ';
");
    }
}

[Collection("TestFixture")]
public class CsvUploadTests(TestFixture test)
{
    [Fact]
    public async Task Test_csv_mixed_delimiter_upload_test1()
    {
        var fileName = "test-mixed-delimiter-upload.csv";
        var sb = new StringBuilder();
        sb.AppendLine("11\tXXX,333");
        sb.AppendLine("12;YYY\t666");
        sb.AppendLine("13,;999");
        var csvContent = sb.ToString();
        var contentBytes = Encoding.UTF8.GetBytes(csvContent);
        using var formData = new MultipartFormDataContent();
        using var byteContent = new ByteArrayContent(contentBytes);
        byteContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        formData.Add(byteContent, "file", fileName);
        using var result = await test.Client.PostAsync("/api/csv-mixed-delimiter-upload/", formData);
        var response = await result.Content.ReadAsStringAsync();
        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var jsonDoc = JsonDocument.Parse(response);
        var rootElement = jsonDoc.RootElement[0]; // Get the first object in the array
        rootElement.GetProperty("type").GetString().Should().Be("csv");
        rootElement.GetProperty("fileName").GetString().Should().Be(fileName);
        rootElement.GetProperty("contentType").GetString().Should().Be("text/csv");
        rootElement.GetProperty("status").GetString().Should().Be("Ok");

        using var connection = Database.CreateConnection();
        await connection.OpenAsync();
        using var command = new NpgsqlCommand("select * from csv_mixed_delimiter_upload_table", connection);
        using var reader = await command.ExecuteReaderAsync();
        var data = new List<(int id, string name, int value)>();
        int idx = 0;
        while (await reader.ReadAsync())
        {
            idx++;
            if (idx == 1)
            {
                reader.GetInt32(0).Should().Be(1);
                reader.GetInt32(1).Should().Be(11);
                reader.GetString(2).Should().Be("XXX");
                reader.GetInt32(3).Should().Be(333);
            }
            if (idx == 2)
            {
                reader.GetInt32(0).Should().Be(2);
                reader.GetInt32(1).Should().Be(12);
                reader.GetString(2).Should().Be("YYY");
                reader.GetInt32(3).Should().Be(666);
            }
            if (idx == 3)
            {
                reader.GetInt32(0).Should().Be(3);
                reader.GetInt32(1).Should().Be(13);
                reader.IsDBNull(2).Should().BeTrue();
                reader.GetInt32(3).Should().Be(999);
            }
        }
        idx.Should().Be(3);
    }


    [Fact]
    public async Task Test_csv_simple_upload_test1()
    {
        var fileName = "test-csv-upload.csv";
        var sb = new StringBuilder();
        sb.AppendLine("Id,Name,Value");
        sb.AppendLine("10,Item 1,666");
        sb.AppendLine("11,,999");
        sb.AppendLine("12,Item 3,");
        var csvContent = sb.ToString();
        var contentBytes = Encoding.UTF8.GetBytes(csvContent);
        using var formData = new MultipartFormDataContent();
        using var byteContent = new ByteArrayContent(contentBytes);
        byteContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        formData.Add(byteContent, "file", fileName);

        using var result = await test.Client.PostAsync("/api/csv-simple-upload/", formData);
        var response = await result.Content.ReadAsStringAsync();
        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var jsonDoc = JsonDocument.Parse(response);
        var rootElement = jsonDoc.RootElement[0]; // Get the first object in the array
        rootElement.GetProperty("type").GetString().Should().Be("csv");
        rootElement.GetProperty("fileName").GetString().Should().Be(fileName);
        rootElement.GetProperty("contentType").GetString().Should().Be("text/csv");
        rootElement.GetProperty("status").GetString().Should().Be("Ok");

        using var connection = Database.CreateConnection();
        await connection.OpenAsync();
        using var command = new NpgsqlCommand("select * from csv_simple_upload_table", connection);
        using var reader = await command.ExecuteReaderAsync();
        var data = new List<(int id, string name, int value, string meta)>();
        
        int idx = 0;
        while (await reader.ReadAsync())
        {
            idx++;
            if (idx == 1)
            {
                reader.GetInt32(0).Should().Be(2);
                reader.GetInt32(1).Should().Be(10);
                reader.GetString(2).Should().Be("Item 1");
                reader.GetInt32(3).Should().Be(666);
                reader.GetInt32(4).Should().Be(1);
                reader.GetString(5).Should().StartWith("{\"type\":\"csv\",\"fileName\":\"test-csv-upload.csv\",\"contentType\":\"text/csv\",\"size\"");
            }
            if (idx == 2)
            {
                reader.GetInt32(0).Should().Be(3);
                reader.GetInt32(1).Should().Be(11);
                reader.IsDBNull(2).Should().BeTrue();
                reader.GetInt32(3).Should().Be(999);
                reader.GetInt32(4).Should().Be(2);
                reader.GetString(5).Should().StartWith("{\"type\":\"csv\",\"fileName\":\"test-csv-upload.csv\",\"contentType\":\"text/csv\",\"size\"");
            }
            if (idx == 3)
            {
                reader.GetInt32(0).Should().Be(4);
                reader.GetInt32(1).Should().Be(12);
                reader.GetString(2).Should().Be("Item 3");
                reader.IsDBNull(3).Should().BeTrue();
                reader.GetInt32(4).Should().Be(3);
                reader.GetString(5).Should().StartWith("{\"type\":\"csv\",\"fileName\":\"test-csv-upload.csv\",\"contentType\":\"text/csv\",\"size\"");
            }
        }
        idx.Should().Be(3);
    }

    [Fact]
    public async Task Test_csv_fallback_to_large_object()
    {
        // Upload binary content to a CSV endpoint with fallback_handler = large_object
        var binaryContent = new byte[] { 0x00, 0x01, 0x02, 0xFF, 0xFE, 0xFD, 0x80, 0x90, 0xA0, 0xB0, 0xC0, 0xD0, 0xE0, 0xF0 };
        using var formData = new MultipartFormDataContent();
        using var byteContent = new ByteArrayContent(binaryContent);
        byteContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        formData.Add(byteContent, "file", "binary-data.bin");

        using var result = await test.Client.PostAsync("/api/csv-fallback-lo-upload/", formData);
        var response = await result.Content.ReadAsStringAsync();
        result.StatusCode.Should().Be(HttpStatusCode.OK);

        // Should have fallen back to large_object handler
        var jsonDoc = JsonDocument.Parse(response);
        var rootElement = jsonDoc.RootElement[0];
        rootElement.GetProperty("type").GetString().Should().Be("large_object");
        rootElement.GetProperty("fileName").GetString().Should().Be("binary-data.bin");
        rootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        rootElement.GetProperty("oid").ValueKind.Should().Be(JsonValueKind.Number);

        // Verify the large object exists in PostgreSQL
        var oid = rootElement.GetProperty("oid").GetInt64();
        using var connection = Database.CreateConnection();
        await connection.OpenAsync();
        using var command = new NpgsqlCommand($"select lo_get({oid})", connection);
        using var transaction = await connection.BeginTransactionAsync();
        var loData = await command.ExecuteScalarAsync() as byte[];
        loData.Should().NotBeNull();
        loData!.Length.Should().Be(binaryContent.Length);
    }

    [Fact]
    public async Task Test_csv_fallback_valid_csv_still_processes_normally()
    {
        // Upload valid CSV to endpoint with fallback_handler configured - should NOT fall back
        var sb = new StringBuilder();
        sb.AppendLine("Id,Name,Value");
        sb.AppendLine("20,Fallback Test,777");
        var csvContent = Encoding.UTF8.GetBytes(sb.ToString());
        using var formData = new MultipartFormDataContent();
        using var byteContent = new ByteArrayContent(csvContent);
        byteContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        formData.Add(byteContent, "file", "valid.csv");

        using var result = await test.Client.PostAsync("/api/csv-fallback-lo-upload/", formData);
        var response = await result.Content.ReadAsStringAsync();
        result.StatusCode.Should().Be(HttpStatusCode.OK);

        // Should process as CSV, not fall back
        var jsonDoc = JsonDocument.Parse(response);
        var rootElement = jsonDoc.RootElement[0];
        rootElement.GetProperty("type").GetString().Should().Be("csv");
        rootElement.GetProperty("success").GetBoolean().Should().BeTrue();

        // Verify the CSV row was inserted
        using var connection = Database.CreateConnection();
        await connection.OpenAsync();
        using var command = new NpgsqlCommand("select * from csv_fallback_lo_upload_table where id = 20", connection);
        using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        reader.GetInt32(1).Should().Be(20);
        reader.GetString(2).Should().Be("Fallback Test");
        reader.GetInt32(3).Should().Be(777);
    }

    [Fact]
    public async Task Test_csv_no_fallback_still_reports_format_error()
    {
        // Upload binary content to CSV endpoint WITHOUT fallback_handler - should report error
        var binaryContent = new byte[] { 0x00, 0x01, 0x02, 0xFF, 0xFE, 0xFD, 0x80, 0x90, 0xA0, 0xB0, 0xC0, 0xD0, 0xE0, 0xF0 };
        using var formData = new MultipartFormDataContent();
        using var byteContent = new ByteArrayContent(binaryContent);
        byteContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        formData.Add(byteContent, "file", "binary-data.bin");

        using var result = await test.Client.PostAsync("/api/csv-no-fallback-upload/", formData);
        var response = await result.Content.ReadAsStringAsync();
        result.StatusCode.Should().Be(HttpStatusCode.OK);

        // Should report format error, no fallback
        var jsonDoc = JsonDocument.Parse(response);
        var rootElement = jsonDoc.RootElement[0];
        rootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        rootElement.GetProperty("status").GetString().Should().Be("ProbablyBinary");
    }
}