namespace NpgsqlRestTests;

public static partial class Database
{
    public static void CommentHttpAttrTests()
    {
        script.Append(@"
create function comment_verb_test1() returns text language sql as 'select ''verb test1''';
comment on function comment_verb_test1() is 'HTTP GET';

create function comment_verb_test2() returns text language sql as 'select ''verb test2''';
comment on function comment_verb_test2() is 'This is some comment.
Some other comment.
HTTP GET
And again some other comment.';

create function comment_verb_test3() returns text language sql as 'select ''verb test3''';
comment on function comment_verb_test3() is 'HTTP GET custom_url_from_comment';

create function comment_wrong_verb() returns text language sql as 'select ''wrong verb''';
comment on function comment_wrong_verb() is 'HTTP wrong-verb';

create function comment_http_path() returns text language sql as 'select ''new path''';
comment on function comment_http_path() is 'HTTP GET
PATH new-path';
");
    }
}

[Collection("TestFixture")]
public class CommentHttpAttrTests(TestFixture test)
{
    [Fact]
    public async Task Test_comment_verb_test1()
    {
        using var response = await test.Client.GetAsync("/api/comment-verb-test1/");
        await AssertResponse(response, "verb test1");
    }

    [Fact]
    public async Task Test_comment_verb_test2()
    {
        using var response = await test.Client.GetAsync("/api/comment-verb-test2/");
        await AssertResponse(response, "verb test2");
    }

    [Fact]
    public async Task Test_comment_verb_test3()
    {
        using var response = await test.Client.GetAsync("/custom_url_from_comment");
        await AssertResponse(response, "verb test3");
    }

    [Fact]
    public async Task Test_comment_wrong_verb()
    {
        using var response1 = await test.Client.GetAsync("/wrong-verb");
        response1?.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);

        using var response2 = await test.Client.PostAsync("/wrong-verb", null);
        //response2?.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await AssertResponse(response2, "wrong verb");

        using var response3 = await test.Client.PostAsync("/api/comment-wrong-verb/", null);
        //await AssertResponse(response3, "wrong verb");
        response3?.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Test_comment_new_path()
    {
        using var response1 = await test.Client.GetAsync("/new-path");
        await AssertResponse(response1, "new path");
    }

    private static async Task AssertResponse(HttpResponseMessage response, string expectedContent)
    {
        var content = await response.Content.ReadAsStringAsync();
        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        response?.Content?.Headers?.ContentType?.MediaType.Should().Be("text/plain");
        content.Should().Be(expectedContent);
    }
}