namespace NpgsqlRestTests;

public static partial class Database
{
    public static void TimeoutTests()
    {
        script.Append(
            """
        create function get_command_timeout1() 
        returns void
        language plpgsql
        as 
        $$
        begin
            perform pg_sleep(2);
        end;
        $$;
        
        comment on function get_command_timeout1() is 'command_timeout 1sec';
        
        create function get_timeout1() 
        returns text
        language plpgsql
        as 
        $$
        begin
            perform pg_sleep(2);
            return 'this will not be returned';
        end;
        $$;
        
        comment on function get_timeout1() is 'timeout 1sec';
        """);
    }
}

[Collection("TestFixture")]
public class TimeoutTests(TestFixture test)
{
    [Fact]
    public async Task Test_get_command_timeout1()
    {
        using var response = await test.Client.GetAsync($"/api/get-command-timeout1/");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.GatewayTimeout);
        content.Should().Be("{\"title\":\"Command execution timed out\",\"status\":504}");
    }
    
    [Fact]
    public async Task Test_get_timeout1()
    {
        using var response = await test.Client.GetAsync($"/api/get-timeout1/");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.GatewayTimeout);
        content.Should().Be("{\"title\":\"Command execution timed out\",\"status\":504}");
    }
}