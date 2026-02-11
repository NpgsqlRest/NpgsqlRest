using System.Net.Http.Headers;
using System.Text.Json;
using Npgsql;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void CombinedUploadTests()
    {
        script.Append(@"
        create function fs_and_lo_upload(
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

        comment on function fs_and_lo_upload(json) is '
        upload for file_system, large_object
        param _meta is upload metadata
        unique_name = false
        ';

        create function fs_and_lo_exclude_mime_type_upload(
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

        comment on function fs_and_lo_exclude_mime_type_upload(json) is '
        upload for file_system, large_object
        param _meta is upload metadata
        unique_name = false
        large_object_excluded_mime_types = text*
        ';

        create function fs_and_lo_exclusive_upload(
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

        comment on function fs_and_lo_exclusive_upload(json) is '
        upload for file_system, large_object
        param _meta is upload metadata
        unique_name = false
        stop_after_first_success = true
        ';
");
    }
}

[Collection("TestFixture")]
public class CombinedUploadTests(TestFixture test)
{
    [Fact]
    public async Task Test_fs_and_lo_upload_test1()
    {
        var fileName = "fs-and-lo.csv";
        var sb = new StringBuilder();
        sb.AppendLine("Id,Name,Value");
        sb.AppendLine("1,XXX A,600");
        sb.AppendLine("2,ZZZ B,700");
        sb.AppendLine("3,YYY C,800");

        var csvContent = sb.ToString();
        var contentBytes = Encoding.UTF8.GetBytes(csvContent);
        using var formData = new MultipartFormDataContent();
        using var byteContent = new ByteArrayContent(contentBytes);
        byteContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        formData.Add(byteContent, "file", fileName);

        using var result = await test.Client.PostAsync("/api/fs-and-lo-upload/", formData);
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await result.Content.ReadAsStringAsync();

        var jsonDoc = JsonDocument.Parse(response);
        jsonDoc.RootElement.GetArrayLength().Should().Be(2);

        var first = jsonDoc.RootElement[0]; // Get the first object in the array
        first.GetProperty("type").GetString().Should().Be("file_system", "because the type should match the expected value");
        first.GetProperty("fileName").GetString().Should().Be(fileName, "because the fileName should match the expected value");
        first.GetProperty("contentType").GetString().Should().Be("text/csv", "because the contentType should match the expected value");
        first.GetProperty("size").GetInt32().Should().BeGreaterThan(40).And.BeLessThan(60);
        first.GetProperty("success").GetBoolean().Should().Be(true);
        first.GetProperty("status").GetString().Should().Be("Ok");

        var filePath = first.GetProperty("filePath").GetString();

        var fileExtension = Path.GetExtension(filePath);
        Path.GetExtension(filePath).Should().Be(".csv", "because the file extension should be .csv");
        Path.GetFileName(filePath).Should().Be("fs-and-lo.csv");

        Path.GetDirectoryName(filePath).Should().Be("."); // the default path 
        File.Exists(filePath).Should().BeTrue("because the file should exist at the specified path");
        File.ReadAllText(filePath!).Should().Be(csvContent, "because the file content should match the original content");

        var second = jsonDoc.RootElement[1]; 
        second.GetProperty("type").GetString().Should().Be("large_object", "because the type should match the expected value");
        second.GetProperty("fileName").GetString().Should().Be(fileName, "because the fileName should match the expected value");
        second.GetProperty("contentType").GetString().Should().Be("text/csv", "because the contentType should match the expected value");
        second.GetProperty("size").GetInt32().Should().BeGreaterThan(40).And.BeLessThan(60);
        second.GetProperty("oid").ValueKind.Should().Be(JsonValueKind.Number, "because oid should be a number");
        second.GetProperty("oid").TryGetInt32(out _).Should().BeTrue("because oid should be a valid integer");
        second.GetProperty("success").GetBoolean().Should().Be(true);
        second.GetProperty("status").GetString().Should().Be("Ok");

        var oid = second.GetProperty("oid").GetInt32();
        using var connection = Database.CreateConnection();
        await connection.OpenAsync();
        using (var command = new NpgsqlCommand("select * from pg_largeobject_metadata where oid = " + oid, connection))
        {
            using var reader = await command.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue(); // there is a record
        }
        using (var command = new NpgsqlCommand("select convert_from(lo_get(" + oid + "), 'utf8')", connection))
        {
            var content = (string?)await command.ExecuteScalarAsync();
            content.Should().Be(csvContent);
        }
        using (var command = new NpgsqlCommand("select * from pg_largeobject where loid = " + oid + " and convert_from(data, 'utf8') = $1", connection))
        {
            command.Parameters.Add(new NpgsqlParameter()
            {
                NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text,
                Value = csvContent
            });
            using var reader = await command.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue(); // there is a record
        }
    }

    [Fact]
    public async Task Test_fs_and_lo_exclude_mime_type_upload_test1()
    {
        var fileName = "fs-and-not-lo.csv";
        var sb = new StringBuilder();
        sb.AppendLine("fs-and-not-lo");
        sb.AppendLine("1,XXX A,600");
        sb.AppendLine("2,ZZZ B,700");
        sb.AppendLine("3,YYY C,800");
        sb.AppendLine("Line 1");
        sb.AppendLine("Line 2");
        sb.AppendLine("Line 2");

        var csvContent = sb.ToString();
        var contentBytes = Encoding.UTF8.GetBytes(csvContent);
        using var formData = new MultipartFormDataContent();
        using var byteContent = new ByteArrayContent(contentBytes);
        byteContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        formData.Add(byteContent, "file", fileName);

        using var result = await test.Client.PostAsync("/api/fs-and-lo-exclude-mime-type-upload/", formData);
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await result.Content.ReadAsStringAsync();

        var jsonDoc = JsonDocument.Parse(response);
        jsonDoc.RootElement.GetArrayLength().Should().Be(2);

        var first = jsonDoc.RootElement[0]; // Get the first object in the array
        first.GetProperty("type").GetString().Should().Be("file_system", "because the type should match the expected value");
        first.GetProperty("fileName").GetString().Should().Be(fileName, "because the fileName should match the expected value");
        first.GetProperty("contentType").GetString().Should().Be("text/csv", "because the contentType should match the expected value");
        first.GetProperty("size").GetInt32().Should().BeGreaterThan(40).And.BeLessThan(80);
        first.GetProperty("success").GetBoolean().Should().Be(true);
        first.GetProperty("status").GetString().Should().Be("Ok");

        var filePath = first.GetProperty("filePath").GetString();

        var fileExtension = Path.GetExtension(filePath);
        Path.GetExtension(filePath).Should().Be(".csv", "because the file extension should be .csv");
        Path.GetFileName(filePath).Should().Be(fileName);

        Path.GetDirectoryName(filePath).Should().Be("."); // the default path 
        File.Exists(filePath).Should().BeTrue("because the file should exist at the specified path");
        File.ReadAllText(filePath!).Should().Be(csvContent, "because the file content should match the original content");

        var second = jsonDoc.RootElement[1];
        second.GetProperty("type").GetString().Should().Be("large_object", "because the type should match the expected value");
        second.GetProperty("fileName").GetString().Should().Be(fileName, "because the fileName should match the expected value");
        second.GetProperty("contentType").GetString().Should().Be("text/csv", "because the contentType should match the expected value");
        second.GetProperty("size").GetInt32().Should().BeGreaterThan(40).And.BeLessThan(80);
        second.GetProperty("oid").ValueKind.Should().Be(JsonValueKind.Null);
        second.GetProperty("success").GetBoolean().Should().Be(false);
        second.GetProperty("status").GetString().Should().Be("InvalidMimeType");
    }

    [Fact]
    public async Task Test_fs_and_lo_exclusive_upload_test1()
    {
        var fileName = "fs-and-lo-exclusive-upload.csv";
        var sb = new StringBuilder();
        sb.AppendLine("fs-and-lo-exclusive-upload");
        sb.AppendLine("1,XXX A,600");
        sb.AppendLine("2,ZZZ B,700");
        sb.AppendLine("3,YYY C,800");
        sb.AppendLine("Line 1");
        sb.AppendLine("Line 2");
        sb.AppendLine("Line 2");

        var csvContent = sb.ToString();
        var contentBytes = Encoding.UTF8.GetBytes(csvContent);
        using var formData = new MultipartFormDataContent();
        using var byteContent = new ByteArrayContent(contentBytes);
        byteContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        formData.Add(byteContent, "file", fileName);

        using var result = await test.Client.PostAsync("/api/fs-and-lo-exclusive-upload/", formData);
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await result.Content.ReadAsStringAsync();

        var jsonDoc = JsonDocument.Parse(response);
        jsonDoc.RootElement.GetArrayLength().Should().Be(2);

        var first = jsonDoc.RootElement[0]; // Get the first object in the array
        first.GetProperty("type").GetString().Should().Be("file_system", "because the type should match the expected value");
        first.GetProperty("fileName").GetString().Should().Be(fileName, "because the fileName should match the expected value");
        first.GetProperty("contentType").GetString().Should().Be("text/csv", "because the contentType should match the expected value");
        first.GetProperty("size").GetInt32().Should().BeGreaterThan(40).And.BeLessThan(180);
        first.GetProperty("success").GetBoolean().Should().Be(true);
        first.GetProperty("status").GetString().Should().Be("Ok");

        var filePath = first.GetProperty("filePath").GetString();

        var fileExtension = Path.GetExtension(filePath);
        Path.GetExtension(filePath).Should().Be(".csv", "because the file extension should be .csv");
        Path.GetFileName(filePath).Should().Be(fileName);

        Path.GetDirectoryName(filePath).Should().Be("."); // the default path 
        File.Exists(filePath).Should().BeTrue("because the file should exist at the specified path");
        File.ReadAllText(filePath!).Should().Be(csvContent, "because the file content should match the original content");

        var second = jsonDoc.RootElement[1];
        second.GetProperty("type").GetString().Should().Be("large_object", "because the type should match the expected value");
        second.GetProperty("fileName").GetString().Should().Be(fileName, "because the fileName should match the expected value");
        second.GetProperty("contentType").GetString().Should().Be("text/csv", "because the contentType should match the expected value");
        second.GetProperty("size").GetInt32().Should().BeGreaterThan(40).And.BeLessThan(180);
        second.GetProperty("oid").ValueKind.Should().Be(JsonValueKind.Null);
        second.GetProperty("success").GetBoolean().Should().Be(false);
        second.GetProperty("status").GetString().Should().Be("Ignored");
    }
}