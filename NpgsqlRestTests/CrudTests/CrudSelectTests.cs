namespace NpgsqlRestTests;

public static partial class Database
{
    public static void CrudSelectTests()
    {
        script.Append("""
        create table crud_select_tests (
            id serial,
            name text not null,
            some_date date not null,
            status boolean not null
        );

        insert into crud_select_tests
        (id, name, some_date, status)
        values
        (1, 'name1', '2024-01-01', true),
        (2, 'name2', '2024-01-20', false),
        (3, 'name3', '2024-01-25', true);
        """);
    }
}

[Collection("TestFixture")]
public class CrudSelectTests(TestFixture test)
{
    [Fact]
    public async Task Test_crud_select_tests_NoParams()
    {
        using var response = await test.Client.GetAsync("/api/crud-select-tests/");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        content.Should().Be("""
            [
                {"id":1,"name":"name1","someDate":"2024-01-01","status":true},
                {"id":2,"name":"name2","someDate":"2024-01-20","status":false},
                {"id":3,"name":"name3","someDate":"2024-01-25","status":true}
            ]
            """
            .Replace(" ", "")
            .Replace("\n", "")
            .Replace("\r", "")
            .Trim());
    }

    [Fact]
    public async Task Test_crud_select_tests_Id1()
    {
        using var response = await test.Client.GetAsync("/api/crud-select-tests/?id=1");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        content.Should().Be("""
            [
                {"id":1,"name":"name1","someDate":"2024-01-01","status":true}
            ]
            """
            .Replace(" ", "")
            .Replace("\n", "")
            .Replace("\r", "")
            .Trim());
    }

    [Fact]
    public async Task Test_crud_select_tests_Name1()
    {
        using var response = await test.Client.GetAsync("/api/crud-select-tests/?name=name1");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        content.Should().Be("""
            [
                {"id":1,"name":"name1","someDate":"2024-01-01","status":true}
            ]
            """
            .Replace(" ", "")
            .Replace("\n", "")
            .Replace("\r", "")
            .Trim());
    }

    [Fact]
    public async Task Test_crud_select_tests_someDate1()
    {
        using var response = await test.Client.GetAsync("/api/crud-select-tests/?someDate=2024-01-01");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        content.Should().Be("""
            [
                {"id":1,"name":"name1","someDate":"2024-01-01","status":true}
            ]
            """
            .Replace(" ", "")
            .Replace("\n", "")
            .Replace("\r", "")
            .Trim());
    }

    [Fact]
    public async Task Test_crud_select_tests_status1()
    {
        using var response = await test.Client.GetAsync("/api/crud-select-tests/?status=true");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        content.Should().Be("""
            [
                {"id":1,"name":"name1","someDate":"2024-01-01","status":true},
                {"id":3,"name":"name3","someDate":"2024-01-25","status":true}
            ]
            """
            .Replace(" ", "")
            .Replace("\n", "")
            .Replace("\r", "")
            .Trim());
    }

    [Fact]
    public async Task Test_crud_select_tests_IdAndName()
    {
        using var response = await test.Client.GetAsync("/api/crud-select-tests/?id=1&name=name1");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        content.Should().Be("""
            [
                {"id":1,"name":"name1","someDate":"2024-01-01","status":true}
            ]
            """
            .Replace(" ", "")
            .Replace("\n", "")
            .Replace("\r", "")
            .Trim());
    }

    [Fact]
    public async Task Test_crud_select_tests_IdAndDate()
    {
        using var response = await test.Client.GetAsync("/api/crud-select-tests/?id=1&someDate=2024-01-01");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        content.Should().Be("""
            [
                {"id":1,"name":"name1","someDate":"2024-01-01","status":true}
            ]
            """
            .Replace(" ", "")
            .Replace("\n", "")
            .Replace("\r", "")
            .Trim());
    }

    [Fact]
    public async Task Test_crud_select_tests_IdAndStatus()
    {
        using var response = await test.Client.GetAsync("/api/crud-select-tests/?id=1&status=true");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        content.Should().Be("""
            [
                {"id":1,"name":"name1","someDate":"2024-01-01","status":true}
            ]
            """
            .Replace(" ", "")
            .Replace("\n", "")
            .Replace("\r", "")
            .Trim());
    }

    [Fact]
    public async Task Test_crud_select_tests_Id1AndStatusFalse()
    {
        using var response = await test.Client.GetAsync("/api/crud-select-tests/?id=1&status=false");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        content.Should().Be("[]");
    }

    [Fact]
    public async Task Test_crud_select_tests_WrongParam()
    {
        using var response = await test.Client.GetAsync("/api/crud-select-tests/?nonExistingParam=ABC");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Test_crud_select_tests_TryUpdate()
    {
        using var body = new StringContent("{\"id\":1,\"status\":false}", Encoding.UTF8, "application/json");
        using var response = await test.Client.PostAsync("/api/crud-select-tests/", body);
        // no primary keys, no update
        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }
}