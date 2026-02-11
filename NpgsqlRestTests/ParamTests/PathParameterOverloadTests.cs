namespace NpgsqlRestTests;

public static partial class Database
{
    public static void PathParameterOverloadTests()
    {
        script.Append(@"
-- POST overloads with path params
create function path_overload(p_id int) returns text language sql as 'select ''path_overload 1 param: '' || $1';
comment on function path_overload(int) is '
HTTP POST /path-overload/{p_id}
';

create function path_overload(p_id int, name text) returns text language sql as 'select ''path_overload 2 params: '' || $1 || '' '' || $2';
comment on function path_overload(int, text) is '
HTTP POST /path-overload/{p_id}
';

-- GET overloads with path params (tests the query string overload resolution fix)
create function path_qs_overload(p_id int) returns text language sql as 'select ''path_qs_overload 1 param: '' || $1';
comment on function path_qs_overload(int) is '
HTTP GET /path-qs-overload/{p_id}
';

create function path_qs_overload(p_id int, filter text) returns text language sql as 'select ''path_qs_overload 2 params: '' || $1 || '' '' || $2';
comment on function path_qs_overload(int, text) is '
HTTP GET /path-qs-overload/{p_id}
';

-- POST overload + default combo with path params
create function path_overload_default(p_id int) returns text language sql as 'select ''path_overload_default 1: '' || $1';
comment on function path_overload_default(int) is '
HTTP POST /path-overload-default/{p_id}
';

create function path_overload_default(p_id int, name text, active boolean default true) returns text
language sql as 'select ''path_overload_default 3: '' || $1 || '' '' || $2 || '' '' || $3';
comment on function path_overload_default(int, text, boolean) is '
HTTP POST /path-overload-default/{p_id}
';
");
    }
}

[Collection("TestFixture")]
public class PathParameterOverloadTests(TestFixture test)
{
    [Fact]
    public async Task Test_path_overload_NoBodyParams()
    {
        // Should match 1-param version
        using var response = await test.Client.PostAsync("/path-overload/5", new StringContent("{}", Encoding.UTF8, "application/json"));
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("path_overload 1 param: 5");
    }

    [Fact]
    public async Task Test_path_overload_OneBodyParam()
    {
        // Should match 2-param version
        using var response = await test.Client.PostAsync("/path-overload/5", new StringContent("{\"name\":\"test\"}", Encoding.UTF8, "application/json"));
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("path_overload 2 params: 5 test");
    }

    [Fact]
    public async Task Test_path_qs_overload_NoQueryParams()
    {
        // Should match 1-param version
        using var response = await test.Client.GetAsync("/path-qs-overload/5");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("path_qs_overload 1 param: 5");
    }

    [Fact]
    public async Task Test_path_qs_overload_WithQueryParam()
    {
        // Should match 2-param version
        using var response = await test.Client.GetAsync("/path-qs-overload/5?filter=test");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("path_qs_overload 2 params: 5 test");
    }

    [Fact]
    public async Task Test_path_overload_default_NoBodyParams()
    {
        // Should match 1-param version (no body params)
        using var response = await test.Client.PostAsync("/path-overload-default/5", new StringContent("{}", Encoding.UTF8, "application/json"));
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("path_overload_default 1: 5");
    }

    [Fact]
    public async Task Test_path_overload_default_DefaultUsed()
    {
        // 1 body param + 1 path param = 2, no overload with ParamCount=2
        // Stays with 3-param version, name is provided, active uses default
        using var response = await test.Client.PostAsync("/path-overload-default/5", new StringContent("{\"name\":\"x\"}", Encoding.UTF8, "application/json"));
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("path_overload_default 3: 5 x true");
    }

    [Fact]
    public async Task Test_path_overload_default_AllBodyParams()
    {
        // 2 body params + 1 path param = 3, matches 3-param version explicitly
        using var response = await test.Client.PostAsync("/path-overload-default/5", new StringContent("{\"name\":\"x\",\"active\":false}", Encoding.UTF8, "application/json"));
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("path_overload_default 3: 5 x false");
    }
}
