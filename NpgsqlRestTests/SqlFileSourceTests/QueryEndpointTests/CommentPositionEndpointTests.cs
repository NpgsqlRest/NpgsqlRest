namespace NpgsqlRestTests.SqlFileSourceTests;

public static partial class SqlFiles
{
    public static void CommentPositionEndpointTests()
    {
        // Annotation in line comment at top (header position)
        File.WriteAllText(Path.Combine(Dir, "comment_header.sql"), """
            -- @param $1 my_val
            select $1 as result;
            """);

        // Annotation in block comment at top
        File.WriteAllText(Path.Combine(Dir, "comment_block_header.sql"), """
            /* @param $1 my_val */
            select $1 as result;
            """);

        // Annotation after the SQL statement (footer position)
        File.WriteAllText(Path.Combine(Dir, "comment_footer.sql"), """
            select $1 as result;
            -- @param $1 my_val
            """);

        // Annotation as inline comment on the same line
        File.WriteAllText(Path.Combine(Dir, "comment_inline.sql"), """
            select $1 as result; -- @param $1 my_val
            """);
    }
}

[Collection("SqlFileSourceFixture")]
public class CommentPositionEndpointTests(SqlFileSourceTestFixture test)
{
    [Fact]
    public async Task HeaderLineComment_ParamAnnotationWorks()
    {
        using var response = await test.Client.GetAsync("/api/comment-header?my_val=hello");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().Be("[{\"result\":\"hello\"}]");
    }

    [Fact]
    public async Task HeaderBlockComment_ParamAnnotationWorks()
    {
        using var response = await test.Client.GetAsync("/api/comment-block-header?my_val=hello");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().Be("[{\"result\":\"hello\"}]");
    }

    [Fact]
    public async Task FooterComment_ParamAnnotationWorks()
    {
        using var response = await test.Client.GetAsync("/api/comment-footer?my_val=hello");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().Be("[{\"result\":\"hello\"}]");
    }

    [Fact]
    public async Task InlineComment_ParamAnnotationWorks()
    {
        using var response = await test.Client.GetAsync("/api/comment-inline?my_val=hello");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().Be("[{\"result\":\"hello\"}]");
    }
}
