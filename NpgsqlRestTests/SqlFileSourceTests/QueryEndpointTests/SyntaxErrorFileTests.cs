namespace NpgsqlRestTests.SqlFileSourceTests;

public static partial class SqlFiles
{
    public static void SyntaxErrorFileTests()
    {
        // SQL with syntax error — should be skipped, not crash startup
        File.WriteAllText(Path.Combine(Dir, "syntax_error.sql"), """
            selec typo from nonexistent_table;
            """);

        // Valid file alongside the bad one — should still work
        File.WriteAllText(Path.Combine(Dir, "after_error.sql"), """
            select 1 as ok;
            """);
    }
}

[Collection("SqlFileSourceFixture")]
public class SyntaxErrorFileTests(SqlFileSourceTestFixture test)
{
    [Fact]
    public async Task SyntaxErrorFile_Skipped_DoesNotCreateEndpoint()
    {
        using var response = await test.Client.GetAsync("/api/syntax-error");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ValidFileAfterError_StillWorks()
    {
        using var response = await test.Client.GetAsync("/api/after-error");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().Be("[{\"ok\":1}]");
    }
}
