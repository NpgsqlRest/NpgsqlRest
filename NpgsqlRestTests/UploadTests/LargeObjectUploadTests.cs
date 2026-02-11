using System.Net.Http.Headers;
using System.Text.Json;
using Npgsql;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void LargeObjectUploadTests()
    {
        script.Append(@"
        create function lo_simple_upload(
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

        comment on function lo_simple_upload(json) is '
        upload _meta as metadata
        ';

        create function lo_custom_parameter_upload(
            _oid bigint,
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

        comment on function lo_custom_parameter_upload(bigint, json) is '
        upload for large_object
        param _meta is upload metadata
        oid = {_oid}
        ';

        create function lo_upload_raise_exception(
            _oid bigint,
            _meta json = null
        )
        returns json 
        language plpgsql
        as 
        $$
        begin
            raise exception 'failed upload';
            return _meta;
        end;
        $$;

        comment on function lo_upload_raise_exception(bigint, json) is '
        upload for large_object
        param _meta is upload metadata
        oid = {_oid}
        ';

        create function lo_simple_upload_default_paramater(
            _default_upload_metadata json = null
        )
        returns json 
        language plpgsql
        as 
        $$
        begin
            return _default_upload_metadata;
        end;
        $$;

        comment on function lo_simple_upload_default_paramater(json) is 'upload';

        create function lo_simple_upload_context_metadata(
        )
        returns json 
        language plpgsql
        as 
        $$
        begin
            return current_setting('request.upload_metadata', true)::text;
        end;
        $$;

        comment on function lo_simple_upload_context_metadata() is 'upload';
");
    }
}

[Collection("TestFixture")]
public class LargeObjectUploadTests(TestFixture test)
{
    [Fact]
    public async Task Test_lo_simple_upload_test1()
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

        using var result = await test.Client.PostAsync("/api/lo-simple-upload/", formData);
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await result.Content.ReadAsStringAsync();

        var jsonDoc = JsonDocument.Parse(response);
        var rootElement = jsonDoc.RootElement[0]; // Get the first object in the array
        rootElement.GetProperty("type").GetString().Should().Be("large_object", "because the type should match the expected value");
        rootElement.GetProperty("fileName").GetString().Should().Be("test-data.csv", "because the fileName should match the expected value");
        rootElement.GetProperty("contentType").GetString().Should().Be("text/csv", "because the contentType should match the expected value");
        rootElement.GetProperty("size").GetInt32().Should().BeOneOf(53, 57);
        rootElement.GetProperty("oid").ValueKind.Should().Be(JsonValueKind.Number, "because oid should be a number");
        rootElement.GetProperty("oid").TryGetInt32(out _).Should().BeTrue("because oid should be a valid integer");

        var oid = rootElement.GetProperty("oid").GetInt32();
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
    public async Task Test_lo_simple_upload_default_paramater_test()
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

        using var result = await test.Client.PostAsync("/api/lo-simple-upload-default-paramater/", formData);
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await result.Content.ReadAsStringAsync();

        var jsonDoc = JsonDocument.Parse(response);
        var rootElement = jsonDoc.RootElement[0]; // Get the first object in the array
        rootElement.GetProperty("type").GetString().Should().Be("large_object", "because the type should match the expected value");
        rootElement.GetProperty("fileName").GetString().Should().Be("test-data.csv", "because the fileName should match the expected value");
        rootElement.GetProperty("contentType").GetString().Should().Be("text/csv", "because the contentType should match the expected value");
        rootElement.GetProperty("size").GetInt32().Should().BeOneOf(53, 57);
        rootElement.GetProperty("oid").ValueKind.Should().Be(JsonValueKind.Number, "because oid should be a number");
        rootElement.GetProperty("oid").TryGetInt32(out _).Should().BeTrue("because oid should be a valid integer");
    }

    [Fact]
    public async Task Test_lo_simple_upload_context_metadata_test()
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

        using var result = await test.Client.PostAsync("/api/lo-simple-upload-context-metadata/", formData);
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await result.Content.ReadAsStringAsync();

        var jsonDoc = JsonDocument.Parse(response);
        var rootElement = jsonDoc.RootElement[0]; // Get the first object in the array
        rootElement.GetProperty("type").GetString().Should().Be("large_object", "because the type should match the expected value");
        rootElement.GetProperty("fileName").GetString().Should().Be("test-data.csv", "because the fileName should match the expected value");
        rootElement.GetProperty("contentType").GetString().Should().Be("text/csv", "because the contentType should match the expected value");
        rootElement.GetProperty("size").GetInt32().Should().BeOneOf(53, 57);
        rootElement.GetProperty("oid").ValueKind.Should().Be(JsonValueKind.Number, "because oid should be a number");
        rootElement.GetProperty("oid").TryGetInt32(out _).Should().BeTrue("because oid should be a valid integer");

        rootElement.GetProperty("success").GetBoolean().Should().Be(true);
        rootElement.GetProperty("status").GetString().Should().Be("Ok");
    }

    [Fact]
    public async Task Test_lo_simple_upload_meta_param1()
    {
        var fileName = "test-data.csv";
        var sb = new StringBuilder();
        sb.AppendLine("Id,Name,Value");
        sb.AppendLine("5,Item 5,500");
        sb.AppendLine("6,Item 6,600");
        sb.AppendLine("7,Item 7,700");
        var csvContent = sb.ToString();
        var contentBytes = Encoding.UTF8.GetBytes(csvContent);
        using var formData = new MultipartFormDataContent();
        using var byteContent = new ByteArrayContent(contentBytes);
        byteContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        formData.Add(byteContent, "file", fileName);

        using var result = await test.Client.PostAsync("/api/lo-simple-upload/?meta=this_content_is_ignored", formData);
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await result.Content.ReadAsStringAsync();

        var jsonDoc = JsonDocument.Parse(response);
        var rootElement = jsonDoc.RootElement[0]; // Get the first object in the array
        rootElement.GetProperty("type").GetString().Should().Be("large_object", "because the type should match the expected value");
        rootElement.GetProperty("fileName").GetString().Should().Be("test-data.csv", "because the fileName should match the expected value");
        rootElement.GetProperty("contentType").GetString().Should().Be("text/csv", "because the contentType should match the expected value");
        rootElement.GetProperty("size").GetInt32().Should().BeOneOf(53, 57);
        rootElement.GetProperty("oid").ValueKind.Should().Be(JsonValueKind.Number, "because oid should be a number");
        rootElement.GetProperty("oid").TryGetInt32(out _).Should().BeTrue("because oid should be a valid integer");

        rootElement.GetProperty("success").GetBoolean().Should().Be(true);
        rootElement.GetProperty("status").GetString().Should().Be("Ok");

        var oid = rootElement.GetProperty("oid").GetInt32();
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
    public async Task Test_lo_simple_upload_failed1()
    {
        var fileName = "test-data.csv";
        var sb = new StringBuilder();
        sb.AppendLine("Id,Name,Value");
        sb.AppendLine("8,Item 8,500");
        sb.AppendLine("9,Item 9,600");
        sb.AppendLine("10,Item 10,1000");
        var csvContent = sb.ToString();
        var contentBytes = Encoding.UTF8.GetBytes(csvContent);
        using var formData = new MultipartFormDataContent();
        using var byteContent = new ByteArrayContent(contentBytes);
        byteContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        formData.Add(byteContent, "file", fileName);

        using var result = await test.Client.PostAsync("/api/lo-simple-upload/?unknown=unseen", formData);
        result.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var response = await result.Content.ReadAsStringAsync();

        response.Should().BeEmpty();

        using var connection = Database.CreateConnection();
        await connection.OpenAsync();
        using var command = new NpgsqlCommand("select * from pg_largeobject where encode(data, 'hex') = encode($1::bytea, 'hex')", connection);
        command.Parameters.Add(new NpgsqlParameter()
        {
            NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bytea,
            Value = Encoding.UTF8.GetBytes(csvContent)
        });
        using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeFalse(); // there is a NO record, LOB has rolled-back
    }

    [Fact]
    public async Task Test_lo_custom_parameter_upload_oid1()
    {
        var fileName = "test-data.csv";
        var sb = new StringBuilder();
        sb.AppendLine("Id,Name,Value");
        sb.AppendLine("10,Item 10,1000");
        sb.AppendLine("11,Item 11,1100");
        sb.AppendLine("12,Item 12,1200");
        var csvContent = sb.ToString();
        var contentBytes = Encoding.UTF8.GetBytes(csvContent);
        using var formData = new MultipartFormDataContent();
        using var byteContent = new ByteArrayContent(contentBytes);
        byteContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        formData.Add(byteContent, "file", fileName);

        var oid = 1;

        using var result = await test.Client.PostAsync($"/api/lo-custom-parameter-upload/?oid={oid}", formData);
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await result.Content.ReadAsStringAsync();

        var jsonDoc = JsonDocument.Parse(response);
        var rootElement = jsonDoc.RootElement[0]; // Get the first object in the array
        rootElement.GetProperty("type").GetString().Should().Be("large_object", "because the type should match the expected value");
        rootElement.GetProperty("fileName").GetString().Should().Be("test-data.csv", "because the fileName should match the expected value");
        rootElement.GetProperty("contentType").GetString().Should().Be("text/csv", "because the contentType should match the expected value");
        rootElement.GetProperty("oid").GetInt64().Should().Be(1, "because oid should be a 1");
        
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
    public async Task Test_lo_upload_raise_exception1()
    {
        var fileName = "test-data.csv";
        var sb = new StringBuilder();
        sb.AppendLine("11,XXX,a");
        sb.AppendLine("22,YYY,b");
        sb.AppendLine("33,ZZZ,c");
        var csvContent = sb.ToString();

        var contentBytes = Encoding.UTF8.GetBytes(csvContent);
        using var formData = new MultipartFormDataContent();
        using var byteContent = new ByteArrayContent(contentBytes);
        byteContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        formData.Add(byteContent, "file", fileName);

        var oid = 2;
        using var result = await test.Client.PostAsync($"/api/lo-upload-raise-exception/?oid={oid}", formData);
        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        
        //var response = await result.Content.ReadAsStringAsync();
        //response.Should().BeEmpty();

        using var connection = Database.CreateConnection();
        await connection.OpenAsync();
        using (var command1 = new NpgsqlCommand("select * from pg_largeobject where loid = " + oid, connection))
        {
            using var reader1 = await command1.ExecuteReaderAsync();
            (await reader1.ReadAsync()).Should().BeFalse(); // there is a NO record, LOB has rolled-back
        }

        using var command2 = new NpgsqlCommand("select * from pg_largeobject_metadata where oid = " + oid, connection);
        using var reader2 = await command2.ExecuteReaderAsync();
        (await reader2.ReadAsync()).Should().BeFalse();  // there is a NO record, LOB has rolled-back
    }
}