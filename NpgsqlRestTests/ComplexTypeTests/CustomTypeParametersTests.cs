namespace NpgsqlRestTests;

public static partial class Database
{
    public static void CustomTypeParametersTests()
    {
        script.Append(@"

        create type custom_type1 as (value text);
        
        create function get_custom_param_query_1p(
            _i int, 
            _p custom_type1, 
            _t text
        ) 
        returns text language sql as 'select _p.value';

        create function get_custom_param_query_2p(
            _p custom_type1
        ) 
        returns text language sql as 'select _p.value';

        create function get_custom_param_query_3p(
            _i int, 
            _t text,
            _p custom_type1
        ) 
        returns text language sql as 'select _p.value';

        create type custom_type2 as (value1 int, value2 text, value3 bool);

        create function get_custom_param_query_4p(
            _p custom_type2
        ) 
        returns text language sql as 'select _p.value2';

        create function get_custom_param_query_5p(
            _p1 custom_type1,
            _p2 custom_type2
        ) 
        returns text language sql as 'select _p2.value2';

        create function get_custom_param_query_6p(
            _p1 int,
            _p2 custom_type1,
            _p3 custom_type2
        ) 
        returns text language sql as 'select _p1::text || _p3.value2';

        create function get_custom_param_query_7p(
            _p2 custom_type1,
            _p1 int,
            _p3 custom_type2
        ) 
        returns text language sql as 'select _p1::text || _p3.value2';

        create function get_custom_param_query_8p(
            _p0 text,
            _p2 custom_type1,
            _p1 int,
            _p3 custom_type2,
            _p4 text
        ) 
        returns text language sql as 'select _p1::text || _p3.value2';

        create function get_custom_param_query_9p(
            _p2 custom_type1,
            _p3 custom_type2,
            _p0 text,
            _p1 int,
            _p4 text
        ) 
        returns text language sql as 'select _p1::text || _p3.value2';

        create schema custom_param_schema;
        create type custom_param_schema.custom_type as (value1 int, value2 text, value3 bool);
        create function custom_param_schema.get_custom_type(
            _p custom_param_schema.custom_type 
        ) 
        returns text language sql as $$
        select _p.value1::text || ' ' || _p.value2 || ' ' || _p.value3::text;
        $$;

        create type my_request as (
            id int,
            text_value text,
            flag boolean
        );

        create function my_service(
            request my_request
        )
        returns void
        language plpgsql as 
        $$
        begin
            raise info 'id: %, text_value: %, flag: %', request.id, request.text_value, request.flag;
        end;
        $$;

        create function get_my_service(
            request my_request
        )
        returns my_request
        language sql as 
        $$
        select request;
        $$;

        create function get_setof_my_requests(
            request my_request
        )
        returns setof my_request
        language sql as 
        $$
        select request union all select request;
        $$;

        create function get_table_of_my_requests(
            request my_request
        )
        returns table (
            req my_request
        )
        language sql as 
        $$
        select request union all select request;
        $$;

        create function get_mixed_table_of_my_requests(
            request my_request
        )
        returns table (
            a text,
            req my_request,
            b text
        )
        language sql as 
        $$
        select 'a1', request, 'b1'
        union all 
        select 'a2', request, 'b2'
        $$;
");
    }
}

[Collection("TestFixture")]
public class CustomTypeParametersTests(TestFixture test)
{
    [Fact]
    public async Task Test_get_custom_param_query_1p()
    {
        var query = new QueryBuilder
        {
            { "i", "1" },
            { "pValue", "test 123" },
            { "t", "test XYZ" },
        };
        using var response = await test.Client.GetAsync($"/api/get-custom-param-query-1p/{query}");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("test 123");
    }
    
    [Fact]
    public async Task Test_get_custom_param_query_1p_default_param()
    {
        var query = new QueryBuilder
        {
            { "i", "1" },
            { "t", "test XYZ" },
        };
        using var response = await test.Client.GetAsync($"/api/get-custom-param-query-1p/{query}");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be(""); // default value for text is null
    }

    [Fact]
    public async Task Test_get_custom_param_query_1p_2()
    {
        var query = new QueryBuilder
        {
            { "pValue", "test 123" },
            { "i", "1" },
            { "t", "test XYZ" },
        };
        using var response = await test.Client.GetAsync($"/api/get-custom-param-query-1p/{query}");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("test 123");
    }
    
    [Fact]
    public async Task Test_get_custom_param_query_2p()
    {
        var query = new QueryBuilder
        {
            { "pValue", "test 123" },
        };
        using var response = await test.Client.GetAsync($"/api/get-custom-param-query-2p/{query}");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("test 123");
    }

    [Fact]
    public async Task Test_get_custom_param_query_3p()
    {
        var query = new QueryBuilder
        {
            { "pValue", "test 123" },
            { "i", "1" },
            { "t", "test XYZ" },
        };
        using var response = await test.Client.GetAsync($"/api/get-custom-param-query-3p/{query}");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("test 123");
    }

    [Fact]
    public async Task Test_get_custom_param_query_4p()
    {
        var query = new QueryBuilder
        {
            { "pValue1", "1" },
            { "pValue2", "test 123" },
            { "pValue3", "true" },
        };
        using var response = await test.Client.GetAsync($"/api/get-custom-param-query-4p/{query}");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("test 123");
    }

    [Fact]
    public async Task Test_get_custom_param_query_5p()
    {
        var query = new QueryBuilder
        {
            { "p2Value1", "1" },
            { "p2Value2", "test 123" },
            { "p2Value3", "true" },
            { "p1Value", "test XYZ" },
        };
        using var response = await test.Client.GetAsync($"/api/get-custom-param-query-5p/{query}");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("test 123");
    }

    [Fact]
    public async Task Test_get_custom_param_query_5p_2()
    {
        var query = new QueryBuilder
        {
            { "p2Value1", "1" },
            { "p1Value", "test XYZ" },
            { "p2Value3", "true" },
            { "p2Value2", "test 123" },
        };
        using var response = await test.Client.GetAsync($"/api/get-custom-param-query-5p/{query}");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("test 123");
    }

    [Fact]
    public async Task Test_get_custom_param_query_6p()
    {
        var query = new QueryBuilder
        {
            { "p1", "1" },
            { "p2Value", "test XYZ" },
            { "p3Value1", "1" },
            { "p3Value2", "test 123" },
            { "p3Value3", "true" },
            
        };
        using var response = await test.Client.GetAsync($"/api/get-custom-param-query-6p/{query}");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("1test 123");
    }

    [Fact]
    public async Task Test_get_custom_param_query_6p_2()
    {
        var query = new QueryBuilder
        {
            { "p2Value", "test XYZ" },
            { "p3Value1", "1" },
            { "p1", "1" },
            { "p3Value2", "test 123" },
            { "p3Value3", "true" },

        };
        using var response = await test.Client.GetAsync($"/api/get-custom-param-query-6p/{query}");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("1test 123");
    }

    [Fact]
    public async Task Test_get_custom_param_query_7p()
    {
        var query = new QueryBuilder
        {
            { "p1", "1" },
            { "p2Value", "test XYZ" },
            { "p3Value1", "1" },
            { "p3Value2", "test 123" },
            { "p3Value3", "true" },

        };
        using var response = await test.Client.GetAsync($"/api/get-custom-param-query-7p/{query}");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("1test 123");
    }

    [Fact]
    public async Task Test_get_custom_param_query_8p()
    {
        var query = new QueryBuilder
        {
            { "p0", "AAA" },
            { "p1", "1" },
            { "p2Value", "test XYZ" },
            { "p3Value1", "1" },
            { "p3Value2", "test 123" },
            { "p3Value3", "true" },
            { "p4", "BBB" },
        };
        using var response = await test.Client.GetAsync($"/api/get-custom-param-query-8p/{query}");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("1test 123");
    }

    [Fact]
    public async Task Test_get_custom_param_query_9p()
    {
        var query = new QueryBuilder
        {
            { "p0", "AAA" },
            { "p1", "1" },
            { "p2Value", "test XYZ" },
            { "p3Value1", "1" },
            { "p3Value2", "test 123" },
            { "p3Value3", "true" },
            { "p4", "BBB" },
        };
        using var response = await test.Client.GetAsync($"/api/get-custom-param-query-9p/{query}");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("1test 123");
    }

    [Fact]
    public async Task Test_custom_param_schema_get_custom_type()
    {
        var query = new QueryBuilder
        {
            { "pValue1", "1" },
            { "pValue2", "test" },
            { "pValue3", "true" },
        };
        using var response = await test.Client.GetAsync($"/api/custom-param-schema/get-custom-type/{query}");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("1 test true");
    }

    [Fact]
    public async Task Test_my_service()
    {
        using var body = new StringContent("""
        {  
            "requestId": 1,
            "requestTextValue": "test",
            "requestFlag": true
        }
        """, Encoding.UTF8, "application/json");

        using var response = await test.Client.PostAsync("/api/my-service", body);
        response?.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Test_get_my_service()
    {
        var query = new QueryBuilder
        {
            { "requestId", "1" },
            { "requestTextValue", "test" },
            { "requestFlag", "true" },
        };
        using var response = await test.Client.GetAsync($"/api/get-my-service/{query}");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("{\"id\":1,\"textValue\":\"test\",\"flag\":true}");
    }

    [Fact]
    public async Task Test_get_setof_my_requests()
    {
        var query = new QueryBuilder
        {
            { "requestId", "1" },
            { "requestTextValue", "test" },
            { "requestFlag", "true" },
        };
        using var response = await test.Client.GetAsync($"/api/get-setof-my-requests/{query}");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"id\":1,\"textValue\":\"test\",\"flag\":true},{\"id\":1,\"textValue\":\"test\",\"flag\":true}]");
    }

    [Fact]
    public async Task Test_get_table_of_my_requests()
    {
        var query = new QueryBuilder
        {
            { "requestId", "1" },
            { "requestTextValue", "test" },
            { "requestFlag", "true" },
        };
        using var response = await test.Client.GetAsync($"/api/get-table-of-my-requests/{query}");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"id\":1,\"textValue\":\"test\",\"flag\":true},{\"id\":1,\"textValue\":\"test\",\"flag\":true}]");
    }

    [Fact]
    public async Task Test_get_mixed_table_of_my_requests()
    {
        var query = new QueryBuilder
        {
            { "requestId", "1" },
            { "requestTextValue", "test" },
            { "requestFlag", "true" },
        };
        using var response = await test.Client.GetAsync($"/api/get-mixed-table-of-my-requests/{query}");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"a\":\"a1\",\"id\":1,\"textValue\":\"test\",\"flag\":true,\"b\":\"b1\"},{\"a\":\"a2\",\"id\":1,\"textValue\":\"test\",\"flag\":true,\"b\":\"b2\"}]");
    }
}