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
