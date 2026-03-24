using System.Net.Http.Headers;
using NpgsqlRest.UploadHandlers;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void FileSystemUploadTests()
    {
        script.Append(@"
        create function fs_simple_upload(
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

        comment on function fs_simple_upload(json) is '
        upload for file_system
        param _meta is upload metadata
        ';

        create function fs_custom_parameter_upload(
            _path text,
            _file text,
            _unique_name boolean,
            _create_path boolean,
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

        comment on function fs_custom_parameter_upload(text, text, boolean, boolean, json) is '
        upload for file_system
        param _meta is upload metadata
        path = {_path}
        file = {_file}
        unique_name = {_unique_name}
        create_path = {_create_path}
        ';

        create function fs_upload_include_mime_type(
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

        comment on function fs_upload_include_mime_type(json) is '
        upload for file_system
        param _meta is upload metadata
        path = ./test
        file = mime_type.csv
        included_mime_types = image/*, application/*
        ';

        create function fs_upload1(
            _raise_error boolean = false,
            _meta json = null
        )
        returns json 
        language plpgsql
        as 
        $$
        begin
            if _raise_error then
                raise exception 'error';
            end if;
            return _meta;
        end;
        $$;

        comment on function fs_upload1(boolean, json) is '
        upload for file_system
        param _meta is upload metadata
        unique_name = false
        ';
");
    }
}

[Collection("TestFixture")]
public class FileSystemUploadTests(TestFixture test)
{
    [Fact]
    public async Task Test_fs_simple_upload_test1()
    {
        var fileName = "test-data.csv";
        var sb = new StringBuilder();
        sb.AppendLine("Id,Name,Value");
        sb.AppendLine("1,Item 1,100");
        sb.AppendLine("2,Item 2,200");
        sb.AppendLine("3,Item 3,300");
        var csvContent = sb.ToString();
        var contentBytes = Encoding.UTF8.GetBytes(csvContent);
        using var formData = new MultipartFormDataContent();
        using var byteContent = new ByteArrayContent(contentBytes);
        byteContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        formData.Add(byteContent, "file", fileName);

        using var result = await test.Client.PostAsync("/api/fs-simple-upload/", formData);
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await result.Content.ReadAsStringAsync();

        var jsonDoc = JsonDocument.Parse(response);
        var rootElement = jsonDoc.RootElement[0]; // Get the first object in the array
        rootElement.GetProperty("type").GetString().Should().Be("file_system", "because the type should match the expected value");
        rootElement.GetProperty("fileName").GetString().Should().Be("test-data.csv", "because the fileName should match the expected value");
        rootElement.GetProperty("contentType").GetString().Should().Be("text/csv", "because the contentType should match the expected value");
        rootElement.GetProperty("size").GetInt32().Should().BeOneOf(53, 57);

        rootElement.GetProperty("success").GetBoolean().Should().Be(true);
        rootElement.GetProperty("status").GetString().Should().Be("Ok");

        var filePath = rootElement.GetProperty("filePath").GetString();

        var fileExtension = Path.GetExtension(filePath);
        Path.GetExtension(filePath).Should().Be(".csv", "because the file extension should be .csv");
        Path.GetFileName(filePath).Should().NotBe("test-data.csv", "because the file name should be unique");

        Path.GetDirectoryName(filePath).Should().Be("."); // the default path 
        File.Exists(filePath).Should().BeTrue("because the file should exist at the specified path");
        File.ReadAllText(filePath!).Should().Be(csvContent, "because the file content should match the original content");
    }

    [Fact]
    public async Task Test_fs_custom_parameter_upload_test1()
    {
        var fileName = "test-data.csv";
        var sb = new StringBuilder();
        sb.AppendLine("Id,Name,Value");
        sb.AppendLine("1,Item 1,100");
        sb.AppendLine("2,Item 2,200");
        sb.AppendLine("3,Item 3,300");
        var csvContent = sb.ToString();
        var contentBytes = Encoding.UTF8.GetBytes(csvContent);
        using var formData = new MultipartFormDataContent();
        using var byteContent = new ByteArrayContent(contentBytes);
        byteContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        formData.Add(byteContent, "file", fileName);

        var query = new QueryBuilder
        {
            { "path", "./test" },
            { "file", fileName },
            { "uniqueName", "false" },
            { "createPath", "true" },
        };

        using var result = await test.Client.PostAsync($"/api/fs-custom-parameter-upload/{query}", formData);
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await result.Content.ReadAsStringAsync();

        var jsonDoc = JsonDocument.Parse(response);
        var rootElement = jsonDoc.RootElement[0]; // Get the first object in the array
        rootElement.GetProperty("type").GetString().Should().Be("file_system", "because the type should match the expected value");
        rootElement.GetProperty("fileName").GetString().Should().Be("test-data.csv", "because the fileName should match the expected value");
        rootElement.GetProperty("contentType").GetString().Should().Be("text/csv", "because the contentType should match the expected value");
        rootElement.GetProperty("size").GetInt32().Should().BeOneOf(53, 57);
        rootElement.GetProperty("filePath").GetString().Should()
            .BeOneOf("./test/test-data.csv", "./test\\test-data.csv");

        rootElement.GetProperty("success").GetBoolean().Should().Be(true);
        rootElement.GetProperty("status").GetString().Should().Be("Ok");

        var filePath = rootElement.GetProperty("filePath").GetString();

        var fileExtension = Path.GetExtension(filePath);
        Path.GetExtension(filePath).Should().Be(".csv", "because the file extension should be .csv");
        Path.GetFileName(filePath).Should().Be("test-data.csv");

        File.Exists(filePath).Should().BeTrue("because the file should exist at the specified path");
        File.ReadAllText(filePath!).Should().Be(csvContent, "because the file content should match the original content");
    }

    [Fact]
    public async Task Test_fs_upload_test_two_files1()
    {
        var fileName1 = "testfile_1.csv";
        var sb1 = new StringBuilder();
        sb1.AppendLine("Id,Name,Value");
        sb1.AppendLine("1,y1,100");
        sb1.AppendLine("2,y2,200");
        var csvContent1 = sb1.ToString();
        var contentBytes1 = Encoding.UTF8.GetBytes(csvContent1);
        using var byteContent1 = new ByteArrayContent(contentBytes1);
        byteContent1.Headers.ContentType = new MediaTypeHeaderValue("text/csv");

        var fileName2 = "testfile_2.csv";
        var sb2 = new StringBuilder();
        sb2.AppendLine("Id,Name,Value");
        sb2.AppendLine("4,x1,400");
        sb2.AppendLine("5,x2,500");
        var csvContent2 = sb2.ToString();
        var contentBytes2 = Encoding.UTF8.GetBytes(csvContent2);
        using var byteContent2 = new ByteArrayContent(contentBytes2);
        byteContent2.Headers.ContentType = new MediaTypeHeaderValue("text/csv");

        using var formData = new MultipartFormDataContent
        {
            { byteContent1, "file", fileName1 },
            { byteContent2, "file", fileName2 }
        };

        using var result = await test.Client.PostAsync("/api/fs-upload1/", formData);
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await result.Content.ReadAsStringAsync();

        var jsonDoc = JsonDocument.Parse(response);
        jsonDoc.RootElement.GetArrayLength().Should().Be(2, "because two files should be uploaded");

        // Add assertions for the first file
        var rootElement1 = jsonDoc.RootElement[0]; // Get the first object in the array
        rootElement1.GetProperty("type").GetString().Should().Be("file_system", "because the type should match the expected value");
        rootElement1.GetProperty("fileName").GetString().Should().Be(fileName1, "because the fileName should match the expected value");
        rootElement1.GetProperty("contentType").GetString().Should().Be("text/csv", "because the contentType should match the expected value");

        rootElement1.GetProperty("success").GetBoolean().Should().Be(true);
        rootElement1.GetProperty("status").GetString().Should().Be("Ok");

        var filePath1 = rootElement1.GetProperty("filePath").GetString();
        File.ReadAllText(filePath1!).Should().Be(csvContent1, "because the file content should match the original content");

        // Add assertions for the second file
        var rootElement2 = jsonDoc.RootElement[1]; // Get the first object in the array
        rootElement2.GetProperty("type").GetString().Should().Be("file_system", "because the type should match the expected value");
        rootElement2.GetProperty("fileName").GetString().Should().Be(fileName2, "because the fileName should match the expected value");
        rootElement2.GetProperty("contentType").GetString().Should().Be("text/csv", "because the contentType should match the expected value");

        rootElement2.GetProperty("success").GetBoolean().Should().Be(true);
        rootElement2.GetProperty("status").GetString().Should().Be("Ok");

        var filePath2 = rootElement2.GetProperty("filePath").GetString();
        File.ReadAllText(filePath2!).Should().Be(csvContent2, "because the file content should match the original content");
    }

    [Fact]
    public async Task Test_fs_upload_test_two_files_fail_test()
    {
        var fileName1 = "testfile_1_fail.csv";
        var sb1 = new StringBuilder();
        sb1.AppendLine("Id,Name,Value");
        sb1.AppendLine("1,q1,100");
        sb1.AppendLine("2,q2,200");
        var csvContent1 = sb1.ToString();
        var contentBytes1 = Encoding.UTF8.GetBytes(csvContent1);
        using var byteContent1 = new ByteArrayContent(contentBytes1);
        byteContent1.Headers.ContentType = new MediaTypeHeaderValue("text/csv");

        var fileName2 = "testfile_2_fail.csv";
        var sb2 = new StringBuilder();
        sb2.AppendLine("Id,Name,Value");
        sb2.AppendLine("4,w1,400");
        sb2.AppendLine("5,w2,500");
        var csvContent2 = sb2.ToString();
        var contentBytes2 = Encoding.UTF8.GetBytes(csvContent2);
        using var byteContent2 = new ByteArrayContent(contentBytes2);
        byteContent2.Headers.ContentType = new MediaTypeHeaderValue("text/csv");

        using var formData = new MultipartFormDataContent
        {
            { byteContent1, "file", fileName1 },
            { byteContent2, "file", fileName2 }
        };

        using var result = await test.Client.PostAsync("/api/fs-upload1/?raiseError=true", formData);
        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        File.Exists(Path.Combine(new UploadHandlerOptions().FileSystemPath, fileName1))
            .Should().BeFalse("because the file should not exist at the specified path");

        File.Exists(Path.Combine(new UploadHandlerOptions().FileSystemPath, fileName2))
            .Should().BeFalse("because the file should not exist at the specified path");
    }

    [Fact]
    public async Task Test_fs_upload_include_mime_type1()
    {
        var fileName = "test-data.csv";
        var sb = new StringBuilder();
        sb.AppendLine("Id,Name,Value");
        sb.AppendLine("1,Item 1,100");
        sb.AppendLine("2,Item 2,200");
        sb.AppendLine("3,Item 3,300");
        var csvContent = sb.ToString();
        var contentBytes = Encoding.UTF8.GetBytes(csvContent);
        using var formData = new MultipartFormDataContent();
        using var byteContent = new ByteArrayContent(contentBytes);
        byteContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        formData.Add(byteContent, "file", fileName);

        using var result = await test.Client.PostAsync("/api/fs-upload-include-mime-type/", formData);
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await result.Content.ReadAsStringAsync();

        response.Should().StartWith("[{\"type\":\"file_system\",\"fileName\":\"test-data.csv\",\"contentType\":\"text/csv\",\"size\":");
        response.Should().EndWith(",\"success\":false,\"status\":\"InvalidMimeType\"}]");
    }
}