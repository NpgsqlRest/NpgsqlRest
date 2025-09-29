namespace NpgsqlRestTests;

public static partial class Database
{
    public static void RateLimiterTests()
    {
        script.Append("""

        create function get_rate_unlimited1() 
        returns text
        language sql as $$
        select current_user;
        $$;
        
        create function get_rate_limited1() 
        returns text
        language sql as $$
        select current_user;
        $$;
        
        comment on function get_rate_limited1() is 'rate max 2 per second';
""");
    }
}

[Collection("TestFixture")]
public class RateLimiterTests(TestFixture test)
{
    [Fact]
    public async Task Test_get_rate_unlimited1()
    {
        using var request1 = new HttpRequestMessage(HttpMethod.Get, "/api/get-rate-unlimited1");
        using var result1 = await test.Client.SendAsync(request1);
        var response1 = await result1.Content.ReadAsStringAsync();
        
        using var request2 = new HttpRequestMessage(HttpMethod.Get, "/api/get-rate-unlimited1");
        using var result2 = await test.Client.SendAsync(request2);
        var response2 = await result2.Content.ReadAsStringAsync();
        
        using var request3 = new HttpRequestMessage(HttpMethod.Get, "/api/get-rate-unlimited1");
        using var result3 = await test.Client.SendAsync(request3);
        var response3 = await result2.Content.ReadAsStringAsync();
        
        result1.StatusCode.Should().Be(HttpStatusCode.OK);
        response1.Should().Be("postgres");
        
        result2.StatusCode.Should().Be(HttpStatusCode.OK);
        response2.Should().Be("postgres");
        
        result3.StatusCode.Should().Be(HttpStatusCode.OK);
        response3.Should().Be("postgres");
    }
    
    [Fact]
    public async Task Test_get_rate_limited1()
    {
        using var request1 = new HttpRequestMessage(HttpMethod.Get, "/api/get-rate-limited1");
        using var result1 = await test.Client.SendAsync(request1);
        var response1 = await result1.Content.ReadAsStringAsync();
        
        using var request2 = new HttpRequestMessage(HttpMethod.Get, "/api/get-rate-limited1");
        using var result2 = await test.Client.SendAsync(request2);
        var response2 = await result2.Content.ReadAsStringAsync();
        
        using var request3 = new HttpRequestMessage(HttpMethod.Get, "/api/get-rate-limited1");
        using var result3 = await test.Client.SendAsync(request3);
        var response3 = await result2.Content.ReadAsStringAsync();
        
        var options  = new RateLimiterOptions();
        result1.StatusCode.Should().Be(HttpStatusCode.OK);
        response1.Should().Be("postgres");
        
        result2.StatusCode.Should().Be(HttpStatusCode.OK);
        response2.Should().Be("postgres");
        
        result3.StatusCode.Should().Be(HttpStatusCode.OK);
        response3.Should().Be("postgres");
    }
}

