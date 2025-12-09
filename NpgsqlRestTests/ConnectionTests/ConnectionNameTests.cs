namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ConnectionNameTests()
    {
        script.Append(@"
create function get_default_connection_name() 
returns text 
language sql
as $$
select application_name 
from pg_stat_activity 
where pid = pg_backend_pid();
$$;

create function get_conn1_connection_name() 
returns text 
language sql
as $$
select application_name 
from pg_stat_activity 
where pid = pg_backend_pid();
$$;

comment on function get_conn1_connection_name() is 'connection_name conn1';

create function get_conn2_connection_name() 
returns text 
language sql
as $$
select application_name 
from pg_stat_activity 
where pid = pg_backend_pid();
$$;

comment on function get_conn2_connection_name() is 'connection_name conn2';

create function get_conn3_connection_name() 
returns text 
language sql
as $$
select application_name 
from pg_stat_activity 
where pid = pg_backend_pid();
$$;

comment on function get_conn3_connection_name() is 'connection_name conn3';
");
    }
}

[Collection("TestFixture")]
public class ConnectionNameTests(TestFixture test)
{
    [Fact]
    public async Task Test_get_default_connection_name()
    {
        using var response = await test.Client.GetAsync("/api/get-default-connection-name/");
        var content = await response.Content.ReadAsStringAsync();
        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("");
    }

    [Fact]
    public async Task Test_get_conn1_connection_name()
    {
        using var response = await test.Client.GetAsync("/api/get-conn1-connection-name/");
        var content = await response.Content.ReadAsStringAsync();
        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("conn1");
    }

    [Fact]
    public async Task Test_get_conn2_connection_name()
    {
        using var response = await test.Client.GetAsync("/api/get-conn2-connection-name/");
        var content = await response.Content.ReadAsStringAsync();
        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("conn2");
    }

    [Fact]
    public async Task Test_get_conn3_connection_name()
    {
        using var response = await test.Client.GetAsync("/api/get-conn3-connection-name/");
        var content = await response.Content.ReadAsStringAsync();
        response?.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        content.Should().Be("Connection name conn3 could not be found in options ConnectionStrings dictionary.");
    }
}