using System.Net.Http.Headers;
using Npgsql;

namespace NpgsqlRestTests;

public static partial class Database
{
    // Setup for CsvUploadMetaParamTypeTests. Each endpoint's row_command passes the per-file metadata
    // as $4 - the binding CsvUploadHandler hardcodes to NpgsqlDbType.Json (CsvUploadHandler.cs:85).
    // The three process-row functions declare that 4th parameter as json / jsonb / text respectively.
    // Today only the json variant resolves; jsonb and text throw 42883. After the Json->Unknown fix all
    // three must succeed. (Auto-invoked by the Database static constructor.)
    public static void CsvUploadMetaParamTypeSetup()
    {
        script.Append(@"
        create table csv_meta_json_tbl  (idx int, meta text);
        create table csv_meta_jsonb_tbl (idx int, meta text);
        create table csv_meta_text_tbl  (idx int, meta text);

        create function csv_meta_json_row(_index int, _row text[], _prev int, _meta json)
        returns int language plpgsql as $$
        begin insert into csv_meta_json_tbl(idx, meta) values (_index, _meta::text); return _index; end;
        $$;
        create function csv_meta_jsonb_row(_index int, _row text[], _prev int, _meta jsonb)
        returns int language plpgsql as $$
        begin insert into csv_meta_jsonb_tbl(idx, meta) values (_index, _meta::text); return _index; end;
        $$;
        create function csv_meta_text_row(_index int, _row text[], _prev int, _meta text)
        returns int language plpgsql as $$
        begin insert into csv_meta_text_tbl(idx, meta) values (_index, _meta); return _index; end;
        $$;

        create function csv_meta_json_upload(_meta json = null) returns json language plpgsql as $$
        begin return _meta; end; $$;
        comment on function csv_meta_json_upload(json) is '
        upload for csv
        param _meta is upload metadata
        row_command = select csv_meta_json_row($1,$2,$3,$4)
        ';

        create function csv_meta_jsonb_upload(_meta json = null) returns json language plpgsql as $$
        begin return _meta; end; $$;
        comment on function csv_meta_jsonb_upload(json) is '
        upload for csv
        param _meta is upload metadata
        row_command = select csv_meta_jsonb_row($1,$2,$3,$4)
        ';

        create function csv_meta_text_upload(_meta json = null) returns json language plpgsql as $$
        begin return _meta; end; $$;
        comment on function csv_meta_text_upload(json) is '
        upload for csv
        param _meta is upload metadata
        row_command = select csv_meta_text_row($1,$2,$3,$4)
        ';
        ");
    }
}

/// <summary>
/// Real end-to-end coverage for the row_command's $4 metadata binding in <c>CsvUploadHandler</c>.
/// The handler hardcodes <c>NpgsqlDbType.Json</c> for $4, so a row_command whose function declares that
/// parameter as <c>jsonb</c> or <c>text</c> fails with PostgreSQL 42883 today - even though the
/// external-auth/upload docs imply text/json/jsonb are interchangeable. These tests assert the TARGET
/// behaviour: all three resolve and the metadata round-trips. Before the Json->Unknown fix the json case
/// passes (regression guard) while the jsonb/text cases fail; after the fix all three pass.
/// </summary>
[Collection("TestFixture")]
public class CsvUploadMetaParamTypeTests(TestFixture test)
{
    private static MultipartFormDataContent BuildCsv(string fileName)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Id,Name,Value");
        sb.AppendLine("10,Item 1,666");
        sb.AppendLine("11,Item 2,999");
        var contentBytes = Encoding.UTF8.GetBytes(sb.ToString());
        var formData = new MultipartFormDataContent();
        var byteContent = new ByteArrayContent(contentBytes);
        byteContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        formData.Add(byteContent, "file", fileName);
        return formData;
    }

    private static async Task<int> StoredFileNameCount(string table, string fileName)
    {
        using var connection = Database.CreateConnection();
        await connection.OpenAsync();
        // meta is stored as text in every variant; parse it as jsonb to read fileName uniformly.
        using var command = new NpgsqlCommand(
            $"select count(*) from {table} where (meta::jsonb)->>'fileName' = @f", connection);
        command.Parameters.AddWithValue("f", fileName);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task RowCommand_Meta_Json_Works()
    {
        const string fileName = "meta-json.csv";
        using var formData = BuildCsv(fileName);
        using var result = await test.Client.PostAsync("/api/csv-meta-json-upload/", formData);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        (await StoredFileNameCount("csv_meta_json_tbl", fileName)).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RowCommand_Meta_Jsonb_Works()
    {
        const string fileName = "meta-jsonb.csv";
        using var formData = BuildCsv(fileName);
        using var result = await test.Client.PostAsync("/api/csv-meta-jsonb-upload/", formData);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        (await StoredFileNameCount("csv_meta_jsonb_tbl", fileName)).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RowCommand_Meta_Text_Works()
    {
        const string fileName = "meta-text.csv";
        using var formData = BuildCsv(fileName);
        using var result = await test.Client.PostAsync("/api/csv-meta-text-upload/", formData);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        (await StoredFileNameCount("csv_meta_text_tbl", fileName)).Should().BeGreaterThan(0);
    }
}
