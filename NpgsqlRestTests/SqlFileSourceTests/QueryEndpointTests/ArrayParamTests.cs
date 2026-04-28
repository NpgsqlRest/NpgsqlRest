namespace NpgsqlRestTests.SqlFileSourceTests;

public static partial class SqlFiles
{
    public static void ArrayParamTests()
    {
        File.WriteAllText(Path.Combine(Dir, "get_by_ids.sql"), """
            -- @param $1 ids integer[]
            select id, name, active from sql_describe_test where id = any($1) order by id;
            """);

        File.WriteAllText(Path.Combine(Dir, "get_by_names.sql"), """
            -- @param $1 names text[]
            select id, name, active from sql_describe_test where name = any($1) order by id;
            """);

        File.WriteAllText(Path.Combine(Dir, "post_by_ids.sql"), """
            -- HTTP POST
            -- @param $1 ids integer[]
            select id, name, active from sql_describe_test where id = any($1) order by id;
            """);

        File.WriteAllText(Path.Combine(Dir, "post_by_names.sql"), """
            -- HTTP POST
            -- @param $1 names text[]
            select id, name, active from sql_describe_test where name = any($1) order by id;
            """);
    }
}

[Collection("SqlFileSourceFixture")]
public class ArrayParamTests(SqlFileSourceTestFixture test)
{
    [Fact]
    public async Task GetByIds_SingleId_ReturnsSingleRow()
    {
        using var response = await test.Client.GetAsync("/api/get-by-ids/?ids=1");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("""[{"id":1,"name":"test1","active":true}]""");
    }

    [Fact]
    public async Task GetByIds_MultipleIds_ReturnsMatchingRows()
    {
        using var response = await test.Client.GetAsync("/api/get-by-ids/?ids=1&ids=2");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("""[{"id":1,"name":"test1","active":true},{"id":2,"name":"test2","active":false}]""");
    }

    [Fact]
    public async Task GetByIds_NoMatch_ReturnsEmptyArray()
    {
        using var response = await test.Client.GetAsync("/api/get-by-ids/?ids=999&ids=998");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("[]");
    }

    [Fact]
    public async Task GetByNames_TextArray_ReturnsMatchingRows()
    {
        using var response = await test.Client.GetAsync("/api/get-by-names/?names=test1&names=test2");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("""[{"id":1,"name":"test1","active":true},{"id":2,"name":"test2","active":false}]""");
    }

    [Fact]
    public async Task PostByIds_MultipleIds_ReturnsMatchingRows()
    {
        using var body = new StringContent("{\"ids\":[1,2]}", Encoding.UTF8, "application/json");
        using var response = await test.Client.PostAsync("/api/post-by-ids/", body);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("""[{"id":1,"name":"test1","active":true},{"id":2,"name":"test2","active":false}]""");
    }

    [Fact]
    public async Task PostByIds_NoMatch_ReturnsEmptyArray()
    {
        using var body = new StringContent("{\"ids\":[999,998]}", Encoding.UTF8, "application/json");
        using var response = await test.Client.PostAsync("/api/post-by-ids/", body);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("[]");
    }

    [Fact]
    public async Task PostByNames_TextArray_ReturnsMatchingRows()
    {
        using var body = new StringContent("{\"names\":[\"test1\",\"test2\"]}", Encoding.UTF8, "application/json");
        using var response = await test.Client.PostAsync("/api/post-by-names/", body);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("""[{"id":1,"name":"test1","active":true},{"id":2,"name":"test2","active":false}]""");
    }
}
