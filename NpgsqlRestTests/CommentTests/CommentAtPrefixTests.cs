namespace NpgsqlRestTests;

public static partial class Database
{
    public static void CommentAtPrefixTests()
    {
        script.Append(@"
-- Test @ prefix for authorize annotation
create function comment_at_authorize1() returns text language sql as 'select ''at_authorize1''';
comment on function comment_at_authorize1() is 'HTTP
@authorize';

-- Test @ prefix for raw annotation
create function comment_at_raw1() returns text language sql as 'select ''col1'';';
comment on function comment_at_raw1() is 'HTTP
@raw';

-- Test @ prefix for cached annotation
create function comment_at_cached1() returns int language sql as 'select 42';
comment on function comment_at_cached1() is 'HTTP
@cached';

-- Test @ prefix combined with regular annotations (mixing styles)
create function comment_at_mixed1() returns text language sql as 'select ''mixed1''';
comment on function comment_at_mixed1() is 'HTTP
@authorize
raw';
");
    }
}

[Collection("TestFixture")]
public class CommentAtPrefixTests(TestFixture test)
{
    [Fact]
    public async Task Test_at_prefix_authorize()
    {
        // @authorize should work the same as authorize
        using var response = await test.Client.PostAsync("/api/comment-at-authorize1/", null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Test_at_prefix_raw()
    {
        // @raw should work the same as raw - returns raw text without JSON formatting
        using var response = await test.Client.PostAsync("/api/comment-at-raw1/", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("col1");
    }

    [Fact]
    public async Task Test_at_prefix_cached()
    {
        // @cached should work the same as cached
        using var response1 = await test.Client.PostAsync("/api/comment-at-cached1/", null);
        response1.StatusCode.Should().Be(HttpStatusCode.OK);
        var content1 = await response1.Content.ReadAsStringAsync();
        content1.Should().Be("42");

        // Second call should return cached result
        using var response2 = await test.Client.PostAsync("/api/comment-at-cached1/", null);
        response2.StatusCode.Should().Be(HttpStatusCode.OK);
        var content2 = await response2.Content.ReadAsStringAsync();
        content2.Should().Be("42");
    }

    [Fact]
    public async Task Test_at_prefix_mixed_with_regular()
    {
        // Mixed @authorize and raw should both work
        using var response = await test.Client.PostAsync("/api/comment-at-mixed1/", null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
