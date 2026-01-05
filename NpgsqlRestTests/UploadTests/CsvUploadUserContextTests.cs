using System.Net.Http.Headers;
using System.Text.Json;
using Npgsql;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void CsvUploadUserContextTests()
    {
        script.Append(@"
        -- Login function for CSV upload user context tests
        create function csv_upload_user_context_login()
        returns table (
            name_identifier int,
            name text,
            role text[]
        )
        language sql as $$
        select
            999 as name_identifier,
            'csv_upload_user' as name,
            array['uploader'] as role
        $$;
        comment on function csv_upload_user_context_login() is 'login';

        -- Table to store uploaded CSV rows with user context
        create table csv_upload_with_user_context_table
        (
            row_index int,
            csv_value text,
            user_id text,
            user_name text
        );

        -- Row processing function that reads user context
        create function csv_upload_with_user_context_process_row(
            _index int,
            _row text[]
        )
        returns int
        language plpgsql
        as
        $$
        begin
            insert into csv_upload_with_user_context_table (
                row_index,
                csv_value,
                user_id,
                user_name
            )
            values (
                _index,
                _row[1],
                current_setting('request.user_id', true),
                current_setting('request.user_name', true)
            );
            return _index;
        end;
        $$;

        -- Upload endpoint function with authorization and user_context
        create function csv_upload_with_user_context(
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

        comment on function csv_upload_with_user_context(json) is '
        authorize
        user_context
        upload for csv
        param _meta is upload metadata
        row_command = select csv_upload_with_user_context_process_row($1,$2)
        ';

        -- Table to store uploaded CSV rows with user params
        create table csv_upload_with_user_params_table
        (
            row_index int,
            csv_value text,
            user_id text,
            user_name text
        );

        -- Row processing function for user params test
        create function csv_upload_with_user_params_process_row(
            _index int,
            _row text[]
        )
        returns int
        language plpgsql
        as
        $$
        begin
            insert into csv_upload_with_user_params_table (
                row_index,
                csv_value,
                user_id,
                user_name
            )
            values (
                _index,
                _row[1],
                current_setting('request.user_id', true),
                current_setting('request.user_name', true)
            );
            return _index;
        end;
        $$;

        -- Upload endpoint function with authorization and user_params
        -- The _user_id and _user_name parameters should be bound from claims
        create function csv_upload_with_user_params(
            _user_id text,
            _user_name text,
            _meta json = null
        )
        returns json
        language plpgsql
        as
        $$
        begin
            return json_build_object(
                'user_id', _user_id,
                'user_name', _user_name,
                'meta', _meta
            );
        end;
        $$;

        comment on function csv_upload_with_user_params(text, text, json) is '
        authorize
        user_params
        user_context
        upload for csv
        param _meta is upload metadata
        row_command = select csv_upload_with_user_params_process_row($1,$2)
        ';

        -- Table to store uploaded CSV rows with claims from metadata
        create table csv_upload_with_claims_metadata_table
        (
            row_index int,
            csv_value text,
            claims_user_id text,
            claims_user_name text
        );

        -- Row processing function that reads claims from metadata JSON ($4)
        create function csv_upload_with_claims_metadata_process_row(
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
            insert into csv_upload_with_claims_metadata_table (
                row_index,
                csv_value,
                claims_user_id,
                claims_user_name
            )
            values (
                _index,
                _row[1],
                _meta->'claims'->>'name_identifier',
                _meta->'claims'->>'name'
            );
            return _index;
        end;
        $$;

        -- Upload endpoint function with authorization that uses 4 params in row_command
        create function csv_upload_with_claims_metadata(
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

        comment on function csv_upload_with_claims_metadata(json) is '
        authorize
        upload for csv
        param _meta is upload metadata
        row_command = select csv_upload_with_claims_metadata_process_row($1,$2,$3,$4)
        ';
");
    }
}

[Collection("TestFixture")]
public class CsvUploadUserContextTests(TestFixture test)
{
    /// <summary>
    /// This test verifies that user context (claims) is properly available
    /// within CSV upload row processing commands when the endpoint has
    /// 'authorize' and 'user_context' annotations.
    ///
    /// The bug: Upload handlers execute before user context is set via SET LOCAL,
    /// so current_setting('request.user_id', true) returns NULL/empty in row_command.
    /// </summary>
    [Fact]
    public async Task Test_csv_upload_with_user_context_should_have_user_claims()
    {
        using var client = test.Application.CreateClient();
        client.Timeout = TimeSpan.FromHours(1);

        // Step 1: Login to get authenticated
        using var login = await client.PostAsync("/api/csv-upload-user-context-login/", null);
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        // Step 2: Upload a CSV file (should have user context available in row_command)
        var fileName = "test-user-context-upload.csv";
        var sb = new StringBuilder();
        sb.AppendLine("ValueA");
        sb.AppendLine("ValueB");
        var csvContent = sb.ToString();
        var contentBytes = Encoding.UTF8.GetBytes(csvContent);

        using var formData = new MultipartFormDataContent();
        using var byteContent = new ByteArrayContent(contentBytes);
        byteContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        formData.Add(byteContent, "file", fileName);

        using var result = await client.PostAsync("/api/csv-upload-with-user-context/", formData);
        var response = await result.Content.ReadAsStringAsync();
        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var jsonDoc = JsonDocument.Parse(response);
        var rootElement = jsonDoc.RootElement[0];
        rootElement.GetProperty("status").GetString().Should().Be("Ok");

        // Step 3: Verify that user context was correctly passed to row_command
        using var connection = Database.CreateConnection();
        await connection.OpenAsync();
        using var command = new NpgsqlCommand(
            "select row_index, csv_value, user_id, user_name from csv_upload_with_user_context_table order by row_index",
            connection);
        using var reader = await command.ExecuteReaderAsync();

        var rows = new List<(int rowIndex, string csvValue, string? userId, string? userName)>();
        while (await reader.ReadAsync())
        {
            rows.Add((
                reader.GetInt32(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)
            ));
        }

        // Should have 2 rows
        rows.Count.Should().Be(2);

        // Row 1
        rows[0].rowIndex.Should().Be(1);
        rows[0].csvValue.Should().Be("ValueA");
        // These assertions will fail if the bug exists - user context is not set during upload
        rows[0].userId.Should().Be("999", "user_id should be set from authenticated user's claims during CSV row processing");
        rows[0].userName.Should().Be("csv_upload_user", "user_name should be set from authenticated user's claims during CSV row processing");

        // Row 2
        rows[1].rowIndex.Should().Be(2);
        rows[1].csvValue.Should().Be("ValueB");
        rows[1].userId.Should().Be("999", "user_id should be set from authenticated user's claims during CSV row processing");
        rows[1].userName.Should().Be("csv_upload_user", "user_name should be set from authenticated user's claims during CSV row processing");
    }

    /// <summary>
    /// This test verifies that user params (claims mapped to function parameters)
    /// are properly bound when the endpoint has 'authorize' and 'user_params' annotations.
    ///
    /// The bug: Upload handlers process files before claim values are bound to parameters,
    /// so the main function receives correct values but row_command cannot access user context.
    /// Also tests that the main function parameters (_user_id, _user_name) are correctly bound.
    /// </summary>
    [Fact]
    public async Task Test_csv_upload_with_user_params_should_have_user_claims()
    {
        using var client = test.Application.CreateClient();
        client.Timeout = TimeSpan.FromHours(1);

        // Step 1: Login to get authenticated
        using var login = await client.PostAsync("/api/csv-upload-user-context-login/", null);
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        // Step 2: Upload a CSV file (should have user params available)
        var fileName = "test-user-params-upload.csv";
        var sb = new StringBuilder();
        sb.AppendLine("ParamValueA");
        sb.AppendLine("ParamValueB");
        var csvContent = sb.ToString();
        var contentBytes = Encoding.UTF8.GetBytes(csvContent);

        using var formData = new MultipartFormDataContent();
        using var byteContent = new ByteArrayContent(contentBytes);
        byteContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        formData.Add(byteContent, "file", fileName);

        using var result = await client.PostAsync("/api/csv-upload-with-user-params/", formData);
        var response = await result.Content.ReadAsStringAsync();
        result.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify the main function received the correct user params
        var jsonDoc = JsonDocument.Parse(response);
        var rootElement = jsonDoc.RootElement;

        // The function returns json_build_object with user_id and user_name from parameters
        // Note: json_build_object returns keys as-is (with underscores), not camelCase
        rootElement.GetProperty("user_id").GetString().Should().Be("999",
            "user_id parameter should be bound from authenticated user's claims");
        rootElement.GetProperty("user_name").GetString().Should().Be("csv_upload_user",
            "user_name parameter should be bound from authenticated user's claims");

        // Step 3: Verify that user context was also correctly set for row_command
        using var connection = Database.CreateConnection();
        await connection.OpenAsync();
        using var command = new NpgsqlCommand(
            "select row_index, csv_value, user_id, user_name from csv_upload_with_user_params_table order by row_index",
            connection);
        using var reader = await command.ExecuteReaderAsync();

        var rows = new List<(int rowIndex, string csvValue, string? userId, string? userName)>();
        while (await reader.ReadAsync())
        {
            rows.Add((
                reader.GetInt32(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)
            ));
        }

        // Should have 2 rows
        rows.Count.Should().Be(2);

        // Row 1
        rows[0].rowIndex.Should().Be(1);
        rows[0].csvValue.Should().Be("ParamValueA");
        // These assertions will fail if the bug exists - user context is not set during upload
        rows[0].userId.Should().Be("999", "user_id should be set from authenticated user's claims during CSV row processing");
        rows[0].userName.Should().Be("csv_upload_user", "user_name should be set from authenticated user's claims during CSV row processing");

        // Row 2
        rows[1].rowIndex.Should().Be(2);
        rows[1].csvValue.Should().Be("ParamValueB");
        rows[1].userId.Should().Be("999", "user_id should be set from authenticated user's claims during CSV row processing");
        rows[1].userName.Should().Be("csv_upload_user", "user_name should be set from authenticated user's claims during CSV row processing");
    }

    /// <summary>
    /// This test verifies that user claims are included in the row metadata JSON parameter ($4)
    /// when the RowCommandUserClaimsKey option is set (default: "claims").
    ///
    /// The row_command can access claims via: _meta->'claims'->>'user_id'
    /// </summary>
    [Fact]
    public async Task Test_csv_upload_should_have_claims_in_metadata_json()
    {
        using var client = test.Application.CreateClient();
        client.Timeout = TimeSpan.FromHours(1);

        // Step 1: Login to get authenticated
        using var login = await client.PostAsync("/api/csv-upload-user-context-login/", null);
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        // Step 2: Upload a CSV file (should have claims in metadata JSON for row_command)
        var fileName = "test-claims-metadata-upload.csv";
        var sb = new StringBuilder();
        sb.AppendLine("MetaValueA");
        sb.AppendLine("MetaValueB");
        var csvContent = sb.ToString();
        var contentBytes = Encoding.UTF8.GetBytes(csvContent);

        using var formData = new MultipartFormDataContent();
        using var byteContent = new ByteArrayContent(contentBytes);
        byteContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        formData.Add(byteContent, "file", fileName);

        using var result = await client.PostAsync("/api/csv-upload-with-claims-metadata/", formData);
        var response = await result.Content.ReadAsStringAsync();
        result.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {response}");

        var jsonDoc = JsonDocument.Parse(response);
        var rootElement = jsonDoc.RootElement[0];
        rootElement.GetProperty("status").GetString().Should().Be("Ok");

        // Step 3: Verify that claims were correctly included in metadata JSON for row_command
        using var connection = Database.CreateConnection();
        await connection.OpenAsync();
        using var command = new NpgsqlCommand(
            "select row_index, csv_value, claims_user_id, claims_user_name from csv_upload_with_claims_metadata_table order by row_index",
            connection);
        using var reader = await command.ExecuteReaderAsync();

        var rows = new List<(int rowIndex, string csvValue, string? claimsUserId, string? claimsUserName)>();
        while (await reader.ReadAsync())
        {
            rows.Add((
                reader.GetInt32(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)
            ));
        }

        // Should have 2 rows
        rows.Count.Should().Be(2);

        // Row 1 - claims should be accessible from metadata JSON via _meta->'claims'->>'user_id'
        rows[0].rowIndex.Should().Be(1);
        rows[0].csvValue.Should().Be("MetaValueA");
        rows[0].claimsUserId.Should().Be("999", "user_id should be accessible from claims in metadata JSON");
        rows[0].claimsUserName.Should().Be("csv_upload_user", "user_name should be accessible from claims in metadata JSON");

        // Row 2
        rows[1].rowIndex.Should().Be(2);
        rows[1].csvValue.Should().Be("MetaValueB");
        rows[1].claimsUserId.Should().Be("999", "user_id should be accessible from claims in metadata JSON");
        rows[1].claimsUserName.Should().Be("csv_upload_user", "user_name should be accessible from claims in metadata JSON");
    }
}
