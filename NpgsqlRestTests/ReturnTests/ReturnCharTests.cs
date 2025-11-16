namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ReturnCharTests()
    {
        script.Append(@"
create function case_return_char1() 
returns char(1)
language plpgsql
as 
$$
begin
    return 'A';
end;
$$;

create function case_return_char_table1() 
returns table(
    c1 char(1),
    c2 char(2),
    c3 char(3)
)
language plpgsql
as 
$$
begin
    return query
    select 
        'A'::char(1),
        'BC'::char(2),
        'DEF'::char(3);
end;
$$;

create type char_type as (
    c1 char(1),
    c2 char(2),
    c3 char(3)
);

create function case_return_char_type1() 
returns char_type
language plpgsql
as 
$$
begin
    return ('A'::char(1), 'BC'::char(2), 'DEF'::char(3));
end;
$$;
");
    }
}

[Collection("TestFixture")]
public class ReturnCharTests(TestFixture test)
{
    [Fact]
    public async Task Test_case_return_char1()
    {
        using var result = await test.Client.PostAsync("/api/case-return-char1/", null);
        var response = await result.Content.ReadAsStringAsync();

        result?.StatusCode.Should().Be(HttpStatusCode.OK);
        result?.Content?.Headers?.ContentType?.MediaType.Should().Be("text/plain");
        response.Should().Be("A");
    }
    
    [Fact]
    public async Task Test_case_return_char_table1()
    {
        using var result = await test.Client.PostAsync("/api/case-return-char-table1/", null);
        var response = await result.Content.ReadAsStringAsync();

        result?.StatusCode.Should().Be(HttpStatusCode.OK);
        result?.Content?.Headers?.ContentType?.MediaType.Should().Be("application/json");
        
        response.Should().Be("[{\"c1\":\"A\",\"c2\":\"BC\",\"c3\":\"DEF\"}]");
    }
    
    [Fact]
    public async Task Test_case_return_char_type1()
    {
        using var result = await test.Client.PostAsync("/api/case-return-char-type1/", null);
        var response = await result.Content.ReadAsStringAsync();

        result?.StatusCode.Should().Be(HttpStatusCode.OK);
        result?.Content?.Headers?.ContentType?.MediaType.Should().Be("application/json");
        
        response.Should().Be("{\"c1\":\"A\",\"c2\":\"BC\",\"c3\":\"DEF\"}");
    }
}