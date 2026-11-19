using System.IO.Compression;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ExcelTableFormatTests()
    {
        script.Append(@"
create function get_excel_table_users()
returns table (id int, name text, email text)
language sql as $$
select * from (values
    (1, 'Alice', 'alice@example.com'),
    (2, 'Bob', 'bob@example.com'),
    (3, 'Charlie', 'charlie@example.com')
) as t(id, name, email);
$$;
comment on function get_excel_table_users() is '
HTTP GET
@table_format = excel
';

create function get_excel_table_named(_name text)
returns table (id int)
language sql as $$ select 1 $$;
comment on function get_excel_table_named(text) is '
HTTP GET
@table_format = excel
excel_file_name = report_{_name}
';

create function get_excel_table_nulls()
returns table (id int, value text)
language sql as $$
select * from (values
    (1, 'has value'),
    (2, null::text),
    (3, 'another value')
) as t(id, value);
$$;
comment on function get_excel_table_nulls() is '
HTTP GET
@table_format = excel
';

create function get_excel_table_empty()
returns table (id int, name text)
language sql as $$
select id, name from (values (1, 'x')) as t(id, name) where false;
$$;
comment on function get_excel_table_empty() is '
HTTP GET
@table_format = excel
';

create function get_excel_no_format()
returns table (id int, name text)
language sql as $$
select * from (values (1, 'test')) as t(id, name);
$$;
comment on function get_excel_no_format() is '
HTTP GET
';

create function get_excel_table_types()
returns table (int_col int, bool_col bool, num_col numeric, text_col text)
language sql as $$
select * from (values
    (42, true, 3.14, 'hello')
) as t(int_col, bool_col, num_col, text_col);
$$;
comment on function get_excel_table_types() is '
HTTP GET
@table_format = excel
';
");
    }
}

[Collection("TestFixture")]
public class ExcelTableFormatTests(TestFixture test)
{
    private const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>
    /// Security regression: a request value in excel_file_name cannot add Content-Disposition parameters. A quote used
    /// to end the quoted filename, so filename*= could choose the saved file's name and extension.
    /// </summary>
    [Fact]
    public async Task Test_excel_file_name_from_request_cannot_inject_header_parameters()
    {
        var name = Uri.EscapeDataString("x\"; filename*=UTF-8''evil.html; y=\"");
        using var result = await test.Client.GetAsync($"/api/get-excel-table-named/?name={name}");

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Content.Headers.ContentDisposition!.ToString().Should().Be(
            "attachment; filename=\"report_x_; filename*=UTF-8''evil.html; y=_.xlsx\"; filename*=UTF-8''report_x_%3B%20filename%2A%3DUTF-8%27%27evil.html%3B%20y%3D_.xlsx");
        result.Content.Headers.ContentDisposition.FileNameStar.Should().Be("report_x_; filename*=UTF-8''evil.html; y=_.xlsx");
    }

    [Fact]
    public async Task Test_excel_table_basic()
    {
        using var result = await test.Client.GetAsync("/api/get-excel-table-users/");

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Content.Headers.ContentType?.MediaType.Should().Be(ExcelContentType);

        // Should have Content-Disposition with .xlsx filename
        var disposition = result.Content.Headers.ContentDisposition;
        disposition.Should().NotBeNull();
        disposition!.DispositionType.Should().Be("attachment");
        disposition.FileName.Should().EndWith(".xlsx");

        // Response body should be a valid ZIP (XLSX is ZIP-based)
        var bytes = await result.Content.ReadAsByteArrayAsync();
        bytes.Length.Should().BeGreaterThan(0);

        // PK zip magic bytes
        bytes[0].Should().Be(0x50); // P
        bytes[1].Should().Be(0x4B); // K

        // Should be a valid ZIP archive containing XLSX parts
        using var stream = new MemoryStream(bytes);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        zip.Entries.Should().NotBeEmpty();
        zip.GetEntry("[Content_Types].xml").Should().NotBeNull();
    }

    [Fact]
    public async Task Test_excel_table_nulls_dont_crash()
    {
        using var result = await test.Client.GetAsync("/api/get-excel-table-nulls/");

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Content.Headers.ContentType?.MediaType.Should().Be(ExcelContentType);

        var bytes = await result.Content.ReadAsByteArrayAsync();
        using var stream = new MemoryStream(bytes);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        zip.GetEntry("[Content_Types].xml").Should().NotBeNull();
    }

    [Fact]
    public async Task Test_excel_table_empty_result()
    {
        using var result = await test.Client.GetAsync("/api/get-excel-table-empty/");

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Content.Headers.ContentType?.MediaType.Should().Be(ExcelContentType);

        // Should still produce a valid XLSX even with no data rows
        var bytes = await result.Content.ReadAsByteArrayAsync();
        bytes.Length.Should().BeGreaterThan(0);
        using var stream = new MemoryStream(bytes);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        zip.GetEntry("[Content_Types].xml").Should().NotBeNull();
    }

    [Fact]
    public async Task Test_no_excel_format_returns_json()
    {
        using var result = await test.Client.GetAsync("/api/get-excel-no-format/");

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        var response = await result.Content.ReadAsStringAsync();
        response.Should().NotContain("PK");
        response.Should().Contain("[");
    }

    [Fact]
    public async Task Test_excel_table_typed_columns()
    {
        using var result = await test.Client.GetAsync("/api/get-excel-table-types/");

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Content.Headers.ContentType?.MediaType.Should().Be(ExcelContentType);

        var bytes = await result.Content.ReadAsByteArrayAsync();
        using var stream = new MemoryStream(bytes);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

        // Verify worksheet XML exists and contains data
        var sheetEntry = zip.GetEntry("xl/worksheets/sheet1.xml");
        sheetEntry.Should().NotBeNull();
        using var sheetStream = sheetEntry!.Open();
        using var reader = new StreamReader(sheetStream);
        var sheetXml = await reader.ReadToEndAsync();

        // Should have row data (at least header row + 1 data row)
        sheetXml.Should().Contain("<row");
    }
}
