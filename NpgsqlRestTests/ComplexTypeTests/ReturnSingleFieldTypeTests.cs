
namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ReturnSingleFieldTypeTests()
    {
        script.Append(@"
        create type single_bool_field_type as (
            status boolean
        );

        create function get_single_bool_field_type() 
        returns single_bool_field_type
        language sql as 
        $$select true;$$;

        create table single_bool_field_tbl (
            status boolean
        );

        create function get_single_bool_field_tbl() 
        returns single_bool_field_tbl
        language sql as 
        $$select true;$$;

        create type single_int_field_type as (
            status int
        );

        create function get_single_int_field_type() 
        returns single_int_field_type
        language sql as 
        $$select 123;$$;

        create type single_int_field_tbl as (
            status int
        );

        create function get_single_int_field_tbl() 
        returns single_int_field_tbl
        language sql as 
        $$select 123;$$;
");
    }
}

[Collection("TestFixture")]
public class ReturnSingleFieldTypeTests(TestFixture test)
{
    [Fact]
    public async Task Test_get_single_bool_field_type()
    {
        using var response = await test.Client.GetAsync($"/api/get-single-bool-field-type");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        response?.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        content.Should().Be("{\"status\":true}");
    }
    
    [Fact]
    public async Task Test_get_single_bool_field_tbl()
    {
        using var response = await test.Client.GetAsync($"/api/get-single-bool-field-tbl");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        response?.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        content.Should().Be("{\"status\":true}");
    }
    
    [Fact]
    public async Task Test_get_single_int_field_type()
    {
        using var response = await test.Client.GetAsync($"/api/get-single-int-field-type");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        response?.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        content.Should().Be("{\"status\":123}");
    }
    
    [Fact]
    public async Task Test_get_single_int_field_tbl()
    {
        using var response = await test.Client.GetAsync($"/api/get-single-int-field-tbl");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        response?.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        content.Should().Be("{\"status\":123}");
    }
}
