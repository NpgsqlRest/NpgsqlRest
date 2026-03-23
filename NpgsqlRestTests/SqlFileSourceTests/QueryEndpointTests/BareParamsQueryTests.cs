namespace NpgsqlRestTests.SqlFileSourceTests;

public static partial class SqlFiles
{
    public static void BareParamsQueryTests()
    {
        File.WriteAllText(Path.Combine(Dir, "bare_params.sql"), """
            select $1, $2
            """);

        File.WriteAllText(Path.Combine(Dir, "bare_params_aliased.sql"), """
            select $1 as val1, $2 as val2
            """);

        File.WriteAllText(Path.Combine(Dir, "bare_params_typed.sql"), """
            select $1::int as num, $2::text as str, $3::boolean as flag
            """);

    }
}

[Collection("SqlFileSourceFixture")]
public class BareParamsQueryTests(SqlFileSourceTestFixture test)
{
    [Fact]
    public async Task BareParams_NoAliases_ReturnsWithQuestionColumnNames()
    {
        using var response = await test.Client.GetAsync("/api/bare-params?$1=hello&$2=world");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        // Unnamed columns get unique fallback names: column1, column2, ...
        content.Should().Be("[{\"column1\":\"hello\",\"column2\":\"world\"}]");
    }

    [Fact]
    public async Task BareParams_WithAliases_ReturnsNamedColumns()
    {
        using var response = await test.Client.GetAsync("/api/bare-params-aliased?$1=hello&$2=world");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().Be("[{\"val1\":\"hello\",\"val2\":\"world\"}]");
    }

    [Fact]
    public async Task BareParams_DifferentTypes_ReturnsCorrectTypes()
    {
        using var response = await test.Client.GetAsync("/api/bare-params-typed?$1=42&$2=hello&$3=true");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");

        using var doc = JsonDocument.Parse(content);
        var row = doc.RootElement[0];
        row.GetProperty("num").GetInt32().Should().Be(42);
        row.GetProperty("str").GetString().Should().Be("hello");
        row.GetProperty("flag").GetBoolean().Should().Be(true);
    }
}
