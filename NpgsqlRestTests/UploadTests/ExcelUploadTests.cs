using System.Net.Http.Headers;
using System.Text.Json;
using Npgsql;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ExcelUploadTests()
    {
        script.Append(@"
        create table excel_simple_upload_table
        (
            index int,
            id int,
            name text,
            value int,
            prev_result int,
            meta json
        );

        create function excel_simple_upload_process_row(
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
            insert into excel_simple_upload_table (
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
            return _index;
        end;
        $$;

        create function excel_simple_upload(
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

        comment on function excel_simple_upload(json) is '
        upload for excel
        param _meta is upload metadata
        row_command = select excel_simple_upload_process_row($1,$2,$3,$4)
        ';

        create table excel_multi_sheet_upload_table
        (
            index int,
            id int,
            name text,
            amount int,
            sheet text,
            meta json
        );

        create function excel_multi_sheet_upload_process_row(
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
            insert into excel_multi_sheet_upload_table (
                index,
                id,
                name,
                amount,
                sheet,
                meta
            )
            values (
                _index,
                _row[1]::int,
                _row[2],
                _row[3]::int,
                _meta->>'sheet',
                _meta
            );
            return _index;
        end;
        $$;

        create function excel_multi_sheet_upload(
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

        comment on function excel_multi_sheet_upload(json) is '
        upload for excel
        param _meta is upload metadata
        all_sheets = true
        row_command = select excel_multi_sheet_upload_process_row($1,$2,$3,$4)
        ';

        create table excel_sheet_filter_upload_table
        (
            index int,
            id int,
            name text,
            amount int,
            sheet text,
            meta json
        );

        create function excel_sheet_filter_upload_process_row(
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
            insert into excel_sheet_filter_upload_table (
                index,
                id,
                name,
                amount,
                sheet,
                meta
            )
            values (
                _index,
                _row[1]::int,
                _row[2],
                _row[3]::int,
                _meta->>'sheet',
                _meta
            );
            return _index;
        end;
        $$;

        create function excel_sheet_filter_upload(
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

        comment on function excel_sheet_filter_upload(json) is '
        upload for excel
        param _meta is upload metadata
        sheet_name = Sales
        row_command = select excel_sheet_filter_upload_process_row($1,$2,$3,$4)
        ';

        create table excel_json_upload_table
        (
            index int,
            row_data json,
            prev_result text,
            meta json
        );

        create function excel_json_upload_process_row(
            _index int,
            _row json,
            _prev_result text,
            _meta json
        )
        returns text
        language plpgsql
        as
        $$
        begin
            insert into excel_json_upload_table (
                index,
                row_data,
                prev_result,
                meta
            )
            values (
                _index,
                _row,
                _prev_result,
                _meta
            );
            return _index::text;
        end;
        $$;

        create function excel_json_upload(
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

        comment on function excel_json_upload(json) is '
        upload for excel
        param _meta is upload metadata
        row_is_json = true
        row_command = select excel_json_upload_process_row($1,$2,$3,$4)
        ';

        create table excel_fallback_upload_table
        (
            index int,
            id int,
            name text,
            value int
        );

        create function excel_fallback_upload_process_row(
            _index int,
            _row text[]
        )
        returns int
        language plpgsql
        as
        $$
        begin
            insert into excel_fallback_upload_table (
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
            return _index;
        end;
        $$;

        create function excel_fallback_upload(
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

        comment on function excel_fallback_upload(json) is '
        upload for excel
        param _meta is upload metadata
        fallback_handler = csv
        row_command = select excel_fallback_upload_process_row($1,$2)
        ';

        create table excel_fallback_excel_ok_table
        (
            index int,
            id int,
            name text,
            value int
        );

        create function excel_fallback_excel_ok_process_row(
            _index int,
            _row text[]
        )
        returns int
        language plpgsql
        as
        $$
        begin
            insert into excel_fallback_excel_ok_table (
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
            return _index;
        end;
        $$;

        create function excel_fallback_excel_ok_upload(
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

        comment on function excel_fallback_excel_ok_upload(json) is '
        upload for excel
        param _meta is upload metadata
        fallback_handler = csv
        row_command = select excel_fallback_excel_ok_process_row($1,$2)
        ';
");
    }
}

[Collection("TestFixture")]
public class ExcelUploadTests(TestFixture test)
{
    private const string ExcelMimeType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static ByteArrayContent LoadTestFile(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "UploadTests", "TestFiles", fileName);
        var bytes = File.ReadAllBytes(path);
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(ExcelMimeType);
        return content;
    }

    [Fact]
    public async Task Test_excel_simple_upload()
    {
        // test-excel-simple.xlsx: single sheet "Sheet1" with 3 data rows (no header):
        // Row 1: 10, Item 1, 666
        // Row 2: 11, (empty), 999
        // Row 3: 12, Item 3, (empty)
        var fileName = "test-excel-simple.xlsx";
        using var formData = new MultipartFormDataContent();
        using var byteContent = LoadTestFile(fileName);
        formData.Add(byteContent, "file", fileName);

        using var result = await test.Client.PostAsync("/api/excel-simple-upload/", formData);
        var response = await result.Content.ReadAsStringAsync();
        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var jsonDoc = JsonDocument.Parse(response);
        var rootElement = jsonDoc.RootElement[0];
        rootElement.GetProperty("type").GetString().Should().Be("excel");
        rootElement.GetProperty("fileName").GetString().Should().Be(fileName);
        rootElement.GetProperty("contentType").GetString().Should().Be(ExcelMimeType);
        rootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        rootElement.GetProperty("sheet").GetString().Should().Be("Sheet1");

        using var connection = Database.CreateConnection();
        await connection.OpenAsync();
        using var command = new NpgsqlCommand("select * from excel_simple_upload_table order by index", connection);
        using var reader = await command.ExecuteReaderAsync();

        int idx = 0;
        while (await reader.ReadAsync())
        {
            idx++;
            if (idx == 1)
            {
                reader.GetInt32(0).Should().Be(1); // index
                reader.GetInt32(1).Should().Be(10); // id
                reader.GetString(2).Should().Be("Item 1"); // name
                reader.GetInt32(3).Should().Be(666); // value
                reader.IsDBNull(4).Should().BeTrue(); // prev_result (first row, no previous)
                reader.GetString(5).Should().StartWith("{\"type\":\"excel\",\"fileName\":\"test-excel-simple.xlsx\"");
            }
            if (idx == 2)
            {
                reader.GetInt32(0).Should().Be(2);
                reader.GetInt32(1).Should().Be(11);
                reader.IsDBNull(2).Should().BeTrue(); // empty cell
                reader.GetInt32(3).Should().Be(999);
                reader.GetInt32(4).Should().Be(1); // prev_result from row 1
            }
            if (idx == 3)
            {
                reader.GetInt32(0).Should().Be(3);
                reader.GetInt32(1).Should().Be(12);
                reader.GetString(2).Should().Be("Item 3");
                reader.IsDBNull(3).Should().BeTrue(); // empty cell
                reader.GetInt32(4).Should().Be(2); // prev_result from row 2
            }
        }
        idx.Should().Be(3);
    }

    [Fact]
    public async Task Test_excel_multi_sheet_all_sheets()
    {
        // test-excel-multi-sheet.xlsx: two sheets
        // Sheet "Sales": Row 1: 1, Widget, 100 | Row 2: 2, Gadget, 200
        // Sheet "Returns": Row 1: 3, Widget, 50
        var fileName = "test-excel-multi-sheet.xlsx";
        using var formData = new MultipartFormDataContent();
        using var byteContent = LoadTestFile(fileName);
        formData.Add(byteContent, "file", fileName);

        using var result = await test.Client.PostAsync("/api/excel-multi-sheet-upload/", formData);
        var response = await result.Content.ReadAsStringAsync();
        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var jsonDoc = JsonDocument.Parse(response);
        // Should have 2 entries in the array (one per sheet)
        jsonDoc.RootElement.GetArrayLength().Should().Be(2);

        var salesElement = jsonDoc.RootElement[0];
        salesElement.GetProperty("sheet").GetString().Should().Be("Sales");
        salesElement.GetProperty("success").GetBoolean().Should().BeTrue();

        var returnsElement = jsonDoc.RootElement[1];
        returnsElement.GetProperty("sheet").GetString().Should().Be("Returns");
        returnsElement.GetProperty("success").GetBoolean().Should().BeTrue();

        using var connection = Database.CreateConnection();
        await connection.OpenAsync();
        using var command = new NpgsqlCommand("select * from excel_multi_sheet_upload_table order by id", connection);
        using var reader = await command.ExecuteReaderAsync();

        int idx = 0;
        while (await reader.ReadAsync())
        {
            idx++;
            if (idx == 1)
            {
                reader.GetInt32(1).Should().Be(1); // id
                reader.GetString(2).Should().Be("Widget"); // name
                reader.GetInt32(3).Should().Be(100); // amount
                reader.GetString(4).Should().Be("Sales"); // sheet
            }
            if (idx == 2)
            {
                reader.GetInt32(1).Should().Be(2);
                reader.GetString(2).Should().Be("Gadget");
                reader.GetInt32(3).Should().Be(200);
                reader.GetString(4).Should().Be("Sales");
            }
            if (idx == 3)
            {
                reader.GetInt32(1).Should().Be(3);
                reader.GetString(2).Should().Be("Widget");
                reader.GetInt32(3).Should().Be(50);
                reader.GetString(4).Should().Be("Returns");
            }
        }
        idx.Should().Be(3);
    }

    [Fact]
    public async Task Test_excel_sheet_name_filter()
    {
        // Upload the multi-sheet file but only process "Sales" sheet
        var fileName = "test-excel-multi-sheet.xlsx";
        using var formData = new MultipartFormDataContent();
        using var byteContent = LoadTestFile(fileName);
        formData.Add(byteContent, "file", fileName);

        using var result = await test.Client.PostAsync("/api/excel-sheet-filter-upload/", formData);
        var response = await result.Content.ReadAsStringAsync();
        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var jsonDoc = JsonDocument.Parse(response);
        // Should have only 1 entry (only Sales sheet)
        jsonDoc.RootElement.GetArrayLength().Should().Be(1);
        jsonDoc.RootElement[0].GetProperty("sheet").GetString().Should().Be("Sales");
        jsonDoc.RootElement[0].GetProperty("success").GetBoolean().Should().BeTrue();

        // Verify only Sales data in database
        using var connection = Database.CreateConnection();
        await connection.OpenAsync();
        using var command = new NpgsqlCommand("select * from excel_sheet_filter_upload_table order by id", connection);
        using var reader = await command.ExecuteReaderAsync();

        int idx = 0;
        while (await reader.ReadAsync())
        {
            idx++;
            reader.GetString(4).Should().Be("Sales"); // all rows should be from Sales sheet
        }
        idx.Should().Be(2); // only Sales sheet rows (2 rows)
    }

    [Fact]
    public async Task Test_excel_json_row_mode()
    {
        // test-excel-json.xlsx: single sheet with 2 data rows:
        // Row 1: Alice, 95
        // Row 2: Bob, 87
        var fileName = "test-excel-json.xlsx";
        using var formData = new MultipartFormDataContent();
        using var byteContent = LoadTestFile(fileName);
        formData.Add(byteContent, "file", fileName);

        using var result = await test.Client.PostAsync("/api/excel-json-upload/", formData);
        var response = await result.Content.ReadAsStringAsync();
        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var jsonDoc = JsonDocument.Parse(response);
        var rootElement = jsonDoc.RootElement[0];
        rootElement.GetProperty("type").GetString().Should().Be("excel");
        rootElement.GetProperty("success").GetBoolean().Should().BeTrue();

        using var connection = Database.CreateConnection();
        await connection.OpenAsync();
        using var command = new NpgsqlCommand("select * from excel_json_upload_table order by index", connection);
        using var reader = await command.ExecuteReaderAsync();

        int idx = 0;
        while (await reader.ReadAsync())
        {
            idx++;
            if (idx == 1)
            {
                reader.GetInt32(0).Should().Be(1); // index
                // JSON row data has cell references like A1, B1
                var rowJson = reader.GetString(1);
                var rowDoc = JsonDocument.Parse(rowJson);
                rowDoc.RootElement.GetProperty("A1").GetString().Should().Be("Alice");
                // Score 95 - ExcelDataReader may read as double
                reader.IsDBNull(2).Should().BeTrue(); // prev_result (first row)
            }
            if (idx == 2)
            {
                reader.GetInt32(0).Should().Be(2);
                var rowJson = reader.GetString(1);
                var rowDoc = JsonDocument.Parse(rowJson);
                rowDoc.RootElement.GetProperty("A2").GetString().Should().Be("Bob");
                reader.GetString(2).Should().Be("1"); // prev_result from row 1
            }
        }
        idx.Should().Be(2);
    }

    [Fact]
    public async Task Test_excel_upload_invalid_format()
    {
        // Upload a non-Excel file content to the excel endpoint without fallback - should return InvalidFormat
        var csvContent = "1,Test,100"u8.ToArray();
        using var formData = new MultipartFormDataContent();
        using var byteContent = new ByteArrayContent(csvContent);
        byteContent.Headers.ContentType = new MediaTypeHeaderValue(ExcelMimeType);
        formData.Add(byteContent, "file", "test.xlsx");

        using var result = await test.Client.PostAsync("/api/excel-simple-upload/", formData);
        var response = await result.Content.ReadAsStringAsync();
        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var jsonDoc = JsonDocument.Parse(response);
        var rootElement = jsonDoc.RootElement[0];
        rootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        rootElement.GetProperty("status").GetString().Should().Be("InvalidFormat");
    }

    [Fact]
    public async Task Test_excel_fallback_to_csv()
    {
        // Upload a CSV file to the excel endpoint with fallback_handler = csv
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("10,Item 1,666");
        sb.AppendLine("11,Item 2,999");
        var csvContent = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        using var formData = new MultipartFormDataContent();
        using var byteContent = new ByteArrayContent(csvContent);
        byteContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        formData.Add(byteContent, "file", "test-data.csv");

        using var result = await test.Client.PostAsync("/api/excel-fallback-upload/", formData);
        var response = await result.Content.ReadAsStringAsync();
        result.StatusCode.Should().Be(HttpStatusCode.OK);

        // The CSV handler should have processed the file
        var jsonDoc = JsonDocument.Parse(response);
        var rootElement = jsonDoc.RootElement[0];
        rootElement.GetProperty("type").GetString().Should().Be("csv");
        rootElement.GetProperty("fileName").GetString().Should().Be("test-data.csv");
        rootElement.GetProperty("success").GetBoolean().Should().BeTrue();

        // Verify rows were inserted by the CSV fallback handler
        using var connection = Database.CreateConnection();
        await connection.OpenAsync();
        using var command = new NpgsqlCommand("select * from excel_fallback_upload_table order by index", connection);
        using var reader = await command.ExecuteReaderAsync();

        int idx = 0;
        while (await reader.ReadAsync())
        {
            idx++;
            if (idx == 1)
            {
                reader.GetInt32(0).Should().Be(1); // index
                reader.GetInt32(1).Should().Be(10); // id
                reader.GetString(2).Should().Be("Item 1"); // name
                reader.GetInt32(3).Should().Be(666); // value
            }
            if (idx == 2)
            {
                reader.GetInt32(0).Should().Be(2);
                reader.GetInt32(1).Should().Be(11);
                reader.GetString(2).Should().Be("Item 2");
                reader.GetInt32(3).Should().Be(999);
            }
        }
        idx.Should().Be(2);
    }

    [Fact]
    public async Task Test_excel_fallback_endpoint_still_handles_excel()
    {
        // Upload a valid Excel file to a fallback endpoint - should process as Excel normally
        var fileName = "test-excel-simple.xlsx";
        using var formData = new MultipartFormDataContent();
        using var byteContent = LoadTestFile(fileName);
        formData.Add(byteContent, "file", fileName);

        using var result = await test.Client.PostAsync("/api/excel-fallback-excel-ok-upload/", formData);
        var response = await result.Content.ReadAsStringAsync();
        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var jsonDoc = JsonDocument.Parse(response);
        var rootElement = jsonDoc.RootElement[0];
        rootElement.GetProperty("type").GetString().Should().Be("excel");
        rootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        rootElement.GetProperty("sheet").GetString().Should().Be("Sheet1");
    }
}
