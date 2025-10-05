namespace NpgsqlRestTests;

public static partial class Database
{
    public static void VolatilityVerbTests()
    {
        script.Append(@"
create function volatile_func() 
returns text 
language sql
volatile
as $$
select 'volatile'
$$;

create function stable_func() 
returns text 
language sql
stable
as $$
select 'stable'
$$;

create function immutable_func() 
returns text 
language sql
immutable
as $$
select 'immutable'
$$;
");
    }
}

[Collection("TestFixture")]
public class VolatilityVerbTests(TestFixture test)
{
    [Fact]
    public async Task Test_volatile_func()
    {
        using var response = await test.Client.PostAsync("/api/volatile-func/", null);
        response?.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Test_stable_func_Post()
    {
        using var response = await test.Client.PostAsync("/api/stable-func/", null);
        response?.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task Test_stable_func_Get()
    {
        using var response = await test.Client.GetAsync("/api/stable-func/");
        response?.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Test_immutable_func_Post()
    {
        using var response = await test.Client.PostAsync("/api/immutable-func/", null);
        response?.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task Test_immutable_func_Get()
    {
        using var response = await test.Client.GetAsync("/api/immutable-func/");
        response?.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}