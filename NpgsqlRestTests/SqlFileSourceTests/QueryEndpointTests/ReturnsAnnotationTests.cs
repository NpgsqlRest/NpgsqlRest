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
}
