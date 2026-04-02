namespace NpgsqlRestTests.SqlFileSourceTests;

public static partial class SqlFiles
{
    public static void ReturnsAnnotationTests()
    {
        // Multi-command with @returns — set_config + DO block creates temp table, @returns skips Describe
        File.WriteAllText(Path.Combine(Dir, "sf_returns_do_block.sql"), """
            -- HTTP GET
            -- @param $1 val1 text
            -- @param $2 val2 integer
            begin;
            select set_config('test.returns_val1', $1, true); -- @skip
            select set_config('test.returns_val2', $2::text, true); -- @skip
            do $$ begin
                create temp table _do_result on commit drop as
                select
                    current_setting('test.returns_val1') as val1,
                    current_setting('test.returns_val2')::int as val2,
                    true as val3;
            end; $$;
            -- @returns sql_file_custom_type
            -- @result data
            -- @single
            select * from _do_result;
            end;
            """);

        // Single-command with @returns — skips Describe entirely
        File.WriteAllText(Path.Combine(Dir, "sf_returns_single_cmd.sql"), """
            -- HTTP GET
            -- @param $1 val1 text
            -- @returns sql_file_custom_type
            -- @single
            select $1 as val1, 42 as val2, true as val3;
            """);

        // @returns with jsonb fields — verify types are preserved
        File.WriteAllText(Path.Combine(Dir, "sf_returns_jsonb.sql"), """
            -- HTTP GET
            -- @returns sql_file_json_type
            -- @single
            select '{"key":"val"}'::jsonb as data, 'hello' as name;
            """);

        // @returns void — skip Describe for a statement that returns nothing
        File.WriteAllText(Path.Combine(Dir, "sf_returns_void_multi.sql"), """
            -- HTTP POST
            -- @param $1 key text
            -- @param $2 value text
            -- @returns void
            select set_config($1, $2, false);
            -- @result data
            select current_setting($1, true) as result;
            """);

        // @returns void on single-command — entire endpoint is void
        File.WriteAllText(Path.Combine(Dir, "sf_returns_void_single.sql"), """
            -- HTTP POST
            -- @param $1 key text
            -- @param $2 value text
            -- @returns void
            select set_config($1, $2, true);
            """);

        // @returns scalar type — single column, only first column used
        File.WriteAllText(Path.Combine(Dir, "sf_returns_int.sql"), """
            -- HTTP GET
            -- @returns integer
            select 42 as val, 'ignored' as other;
            """);

        // @returns scalar type + @single — bare scalar value
        File.WriteAllText(Path.Combine(Dir, "sf_returns_int_single.sql"), """
            -- HTTP GET
            -- @returns integer
            -- @single
            select 42 as val, 'ignored' as other;
            """);

        // @returns text scalar
        File.WriteAllText(Path.Combine(Dir, "sf_returns_text.sql"), """
            -- HTTP GET
            -- @returns text
            select 'hello' as val, 123 as other;
            """);

        // @returns boolean scalar + @single
        File.WriteAllText(Path.Combine(Dir, "sf_returns_bool_single.sql"), """
            -- HTTP GET
            -- @returns boolean
            -- @single
            select true as val, 'ignored' as other;
            """);

        // @returns with multi-command, mixed described and overridden statements
        File.WriteAllText(Path.Combine(Dir, "sf_returns_mixed.sql"), """
            -- HTTP GET
            -- @param $1 name text
            select $1 as greeting;
            -- @returns sql_file_custom_type
            -- @result typed
            select 'hello' as val1, 1 as val2, true as val3;
            """);
    }
}

[Collection("SqlFileSourceFixture")]
public class ReturnsAnnotationTests(SqlFileSourceTestFixture test)
{
    [Fact]
    public async Task ReturnDoBlock_SkipsDescribe_ReturnsData()
    {
        using var response = await test.Client.GetAsync("/api/sf-returns-do-block?val1=hello&val2=42");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, content);

        using var doc = JsonDocument.Parse(content);
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("val1").GetString().Should().Be("hello");
        data.GetProperty("val2").GetInt32().Should().Be(42);
        data.GetProperty("val3").GetBoolean().Should().Be(true);
    }

    [Fact]
    public async Task ReturnSingleCommand_SkipsDescribe_ReturnsData()
    {
        using var response = await test.Client.GetAsync("/api/sf-returns-single-cmd?val1=test");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        doc.RootElement.GetProperty("val1").GetString().Should().Be("test");
        doc.RootElement.GetProperty("val2").GetInt32().Should().Be(42);
        doc.RootElement.GetProperty("val3").GetBoolean().Should().Be(true);
    }

    [Fact]
    public void TsClient_ReturnsJsonb_GeneratesAnyType()
    {
        // Find the generated TsClient file
        var tsFiles = Directory.GetFiles(test.TsClientDir, "*.ts");
        tsFiles.Should().NotBeEmpty("TsClient should generate at least one .ts file");
        var content = string.Join("\n", tsFiles.Select(File.ReadAllText));
        // The @returns sql_file_json_type has jsonb field — TsClient should generate "any" not "string"
        content.Should().Contain("data: any");
    }

    [Fact]
    public async Task ReturnJsonb_TypePreserved()
    {
        using var response = await test.Client.GetAsync("/api/sf-returns-jsonb");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, content);

        using var doc = JsonDocument.Parse(content);
        // jsonb field should be parsed as JSON, not string
        doc.RootElement.GetProperty("data").GetProperty("key").GetString().Should().Be("val");
        doc.RootElement.GetProperty("name").GetString().Should().Be("hello");
    }

    [Fact]
    public async Task ReturnMixed_DescribedAndOverridden_BothWork()
    {
        using var response = await test.Client.GetAsync("/api/sf-returns-mixed?name=world");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, content);

        using var doc = JsonDocument.Parse(content);
        // Second statement used @returns override
        var typed = doc.RootElement.GetProperty("typed");
        typed.EnumerateArray().First().GetProperty("val1").GetString().Should().Be("hello");
    }

    // --- @returns scalar types ---

    [Fact]
    public async Task ReturnsInt_ReturnsIntegerArray()
    {
        using var response = await test.Client.GetAsync("/api/sf-returns-int");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, content);

        content.Should().Be("[42]");
    }

    [Fact]
    public async Task ReturnsIntSingle_ReturnsBareInteger()
    {
        using var response = await test.Client.GetAsync("/api/sf-returns-int-single");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, content);

        content.Should().Be("42");
    }

    [Fact]
    public async Task ReturnsText_ReturnsStringArray()
    {
        using var response = await test.Client.GetAsync("/api/sf-returns-text");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, content);

        content.Should().Be("""["hello"]""");
    }

    [Fact]
    public async Task ReturnsBoolSingle_ReturnsBareBoolean()
    {
        using var response = await test.Client.GetAsync("/api/sf-returns-bool-single");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, content);

        content.Should().Be("true");
    }

    // --- @returns void ---

    [Fact]
    public async Task ReturnsVoid_MultiCommand_SkipsDescribeForVoidStatement()
    {
        using var response = await test.Client.PostAsync("/api/sf-returns-void-multi",
            new StringContent("""{"key":"test.rv_key","value":"rv_val"}""",
                System.Text.Encoding.UTF8, "application/json"));
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, content);

        // First statement is void (@returns void) — produces rows-affected count
        // Second statement returns data with the set value
        content.Should().Contain("rv_val");
    }

    [Fact]
    public async Task ReturnsVoid_SingleCommand_Returns204()
    {
        using var response = await test.Client.PostAsync("/api/sf-returns-void-single",
            new StringContent("""{"key":"test.rv_single","value":"hello"}""",
                System.Text.Encoding.UTF8, "application/json"));
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, content);
    }
}
