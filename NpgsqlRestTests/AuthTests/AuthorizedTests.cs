namespace NpgsqlRestTests;

public static partial class Database
{
    public static void AuthTests()
    {
        script.Append("""
        create function authorized() returns text language sql as 'select ''authorized''';
        comment on function authorized() is 'authorize';

        create function authorized_roles1() returns text language sql as 'select ''roles1''';
        comment on function authorized_roles1() is 'authorize test_role';

        create function authorized_roles2() returns text language sql as 'select ''roles2''';
        comment on function authorized_roles2() is 'authorize test_role, role1';

        create function authorized_roles3() returns text language sql as 'select ''roles3''';
        comment on function authorized_roles3() is 'authorize test_role1 role1 test_role2 test_role1';

        create function authorized_roles4() returns text language sql as 'select ''roles4''';
        comment on function authorized_roles4() is 'authorize test_role1 test_role2 test_role3';
        """);
    }
}

[Collection("TestFixture")]
public class AuthorizedTests(TestFixture test)
{
    [Fact]
    public async Task Test_authorized()
    {
        using var client = test.Application.CreateClient();
        client.Timeout = TimeSpan.FromHours(1);

        using var response1 = await client.PostAsync("/api/authorized/", null);
        response1.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var content1 = await response1.Content.ReadAsStringAsync();
        content1.Should().Contain("\"status\":401");
        content1.Should().Contain("\"title\":\"Unauthorized\"");

        using var login = await client.GetAsync("/login");

        using var response2 = await client.PostAsync("/api/authorized/", null);
        response2.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Test_authorized_roles1()
    {
        using var client = test.Application.CreateClient();
        client.Timeout = TimeSpan.FromHours(1);

        using var response1 = await client.PostAsync("/api/authorized-roles1/", null);
        response1.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var content1 = await response1.Content.ReadAsStringAsync();
        content1.Should().Contain("\"status\":401");
        content1.Should().Contain("\"title\":\"Unauthorized\"");

        using var login = await client.GetAsync("/login");

        using var response2 = await client.PostAsync("/api/authorized-roles1/", null);
        response2.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var content2 = await response2.Content.ReadAsStringAsync();
        content2.Should().Contain("\"status\":403");
        content2.Should().Contain("\"title\":\"Forbidden\"");
    }

    [Fact]
    public async Task Test_authorized_roles2()
    {
        using var client = test.Application.CreateClient();
        client.Timeout = TimeSpan.FromHours(1);

        using var response1 = await client.PostAsync("/api/authorized-roles2/", null);
        response1.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var content1 = await response1.Content.ReadAsStringAsync();
        content1.Should().Contain("\"status\":401");
        content1.Should().Contain("\"title\":\"Unauthorized\"");

        using var login = await client.GetAsync("/login");

        using var response2 = await client.PostAsync("/api/authorized-roles2/", null);
        response2.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Test_authorized_roles3()
    {
        using var client = test.Application.CreateClient();
        client.Timeout = TimeSpan.FromHours(1);

        using var response1 = await client.PostAsync("/api/authorized-roles3/", null);
        response1.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var content1 = await response1.Content.ReadAsStringAsync();
        content1.Should().Contain("\"status\":401");
        content1.Should().Contain("\"title\":\"Unauthorized\"");

        using var login = await client.GetAsync("/login");

        using var response2 = await client.PostAsync("/api/authorized-roles3/", null);
        response2.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Test_authorized_roles4()
    {
        using var client = test.Application.CreateClient();
        client.Timeout = TimeSpan.FromHours(1);

        using var response1 = await client.PostAsync("/api/authorized-roles4/", null);
        response1.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var content1 = await response1.Content.ReadAsStringAsync();
        content1.Should().Contain("\"status\":401");
        content1.Should().Contain("\"title\":\"Unauthorized\"");

        using var login = await client.GetAsync("/login");

        using var response2 = await client.PostAsync("/api/authorized-roles4/", null);
        response2.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var content2 = await response2.Content.ReadAsStringAsync();
        content2.Should().Contain("\"status\":403");
        content2.Should().Contain("\"title\":\"Forbidden\"");
    }
}