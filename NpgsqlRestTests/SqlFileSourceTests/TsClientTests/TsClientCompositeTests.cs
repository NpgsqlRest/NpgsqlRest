namespace NpgsqlRestTests.SqlFileSourceTests;

public static partial class SqlFiles
{
    public static void TsClientCompositeTests()
    {
        // Flat mode: composite mixed with scalar columns
        File.WriteAllText(Path.Combine(Dir, "ts_composite_flat.sql"), """
            -- tsclient_module=ts_composite_flat
            -- @param $1 id
            select id, data, id + 100 as extra from sql_file_custom_table where id = $1;
            """);

        // Nested mode: composite mixed with scalar columns
        File.WriteAllText(Path.Combine(Dir, "ts_composite_nested.sql"), """
            -- nested
            -- tsclient_module=ts_composite_nested
            -- @param $1 id
            select id, data, id + 100 as extra from sql_file_custom_table where id = $1;
            """);
    }
}

[Collection("SqlFileSourceFixture")]
public class TsClientCompositeTests(SqlFileSourceTestFixture test)
{
    private string ReadGeneratedFile()
    {
        var tsFiles = Directory.GetFiles(test.TsClientDir, "*.ts");
        tsFiles.Should().NotBeEmpty($"Expected TsClient output files in {test.TsClientDir}");
        return string.Join("\n", tsFiles.Select(File.ReadAllText));
    }

    [Fact]
    public void TsClient_CompositeFlat_FieldsInlinedInInterface()
    {
        var content = ReadGeneratedFile();

        // Flat mode: composite fields should appear as individual properties
        // matching the actual JSON: {"id":1,"val1":"hello","val2":42,"val3":true,"extra":101}
        // The response interface should have expanded composite fields, NOT "data: string"
        content.Should().Contain("val1: string | null;");
        content.Should().Contain("val2: number | null;");
        content.Should().Contain("val3: boolean | null;");

        // Should NOT have "data" as a standalone string property — it's been expanded
        // Use regex with word boundary to avoid matching "include_data"
        content.Should().NotMatchRegex(@"(?<!\w)data: string \| null;",
            "Composite column 'data' should be expanded to individual fields, not generated as 'string'");
    }

    [Fact]
    public void TsClient_CompositeNested_NestedInterfaceGenerated()
    {
        var content = ReadGeneratedFile();

        // Nested mode: composite should be a nested interface
        // matching the actual JSON: {"id":1,"data":{"val1":"hello","val2":42,"val3":true},"extra":101}
        // Should have a nested "data" property referencing a composite interface
        content.Should().Contain("data: IData | null;");

        // The composite interface should exist with the right fields
        content.Should().Contain("interface IData {");
    }

    [Fact]
    public async Task TsClient_FlatResponse_MatchesInterface()
    {
        // Verify the actual HTTP response matches what TsClient generates
        var content = ReadGeneratedFile();
        using var response = await test.Client.GetAsync("/api/ts-composite-flat?id=1");
        var json = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Response has flat fields: id, val1, val2, val3, extra
        using var doc = JsonDocument.Parse(json);
        var row = doc.RootElement[0];
        var responseFields = row.EnumerateObject().Select(p => p.Name).ToList();

        // The TsClient interface should have matching fields
        foreach (var field in responseFields)
        {
            content.Should().Contain($"{field}:");
        }

        // Verify the response structure matches flat mode
        json.Should().Be("[{\"id\":1,\"val1\":\"hello\",\"val2\":42,\"val3\":true,\"extra\":101}]");
    }

    [Fact]
    public async Task TsClient_NestedResponse_MatchesInterface()
    {
        // Verify the actual HTTP response matches what TsClient generates
        var content = ReadGeneratedFile();
        using var response = await test.Client.GetAsync("/api/ts-composite-nested?id=1");
        var json = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Response has nested structure: id, data: {val1, val2, val3}, extra
        json.Should().Be("[{\"id\":1,\"data\":{\"val1\":\"hello\",\"val2\":42,\"val3\":true},\"extra\":101}]");

        // Interface has matching "data: IData | null;" and IData has the fields
        content.Should().Contain("data: IData | null;");
        content.Should().Contain("interface IData {");
    }
}
