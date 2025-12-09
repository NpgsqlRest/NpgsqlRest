namespace NpgsqlRestTests;

public static partial class Database
{
    public static void CustomTableTypeParametersTests()
    {
        script.Append(@"

        create table custom_table_type1 (value text);
        
        create function get_custom_table_param_query_1p(
            _i int, 
            _p custom_table_type1, 
            _t text
        ) 
        returns text language sql as 'select _p.value';

        create function get_custom_table_param_query_2p(
            _p custom_table_type1
        ) 
        returns text language sql as 'select _p.value';

        create function get_custom_table_param_query_3p(
            _i int, 
            _t text,
            _p custom_table_type1
        ) 
        returns text language sql as 'select _p.value';

        create table custom_table_type2 (value1 int, value2 text, value3 bool);

        create function get_custom_table_param_query_4p(
            _p custom_table_type2
        ) 
        returns text language sql as 'select _p.value2';

        create function get_custom_table_param_query_5p(
            _p1 custom_table_type1,
            _p2 custom_table_type2
        ) 
        returns text language sql as 'select _p2.value2';

        create function get_custom_table_param_query_6p(
            _p1 int,
            _p2 custom_table_type1,
            _p3 custom_table_type2
        ) 
        returns text language sql as 'select _p1::text || _p3.value2';

        create function get_custom_table_param_query_7p(
            _p2 custom_table_type1,
            _p1 int,
            _p3 custom_table_type2
        ) 
        returns text language sql as 'select _p1::text || _p3.value2';

        create function get_custom_table_param_query_8p(
            _p0 text,
            _p2 custom_table_type1,
            _p1 int,
            _p3 custom_table_type2,
            _p4 text
        ) 
        returns text language sql as 'select _p1::text || _p3.value2';

        create function get_custom_table_param_query_9p(
            _p2 custom_table_type1,
            _p3 custom_table_type2,
            _p0 text,
            _p1 int,
            _p4 text
        ) 
        returns text language sql as 'select _p1::text || _p3.value2';

        create schema custom_table_param_schema;
        create table custom_table_param_schema.custom_table_type (value1 int, value2 text, value3 bool);
        create function custom_table_param_schema.get_custom_table_type(
            _p custom_table_param_schema.custom_table_type 
        ) 
        returns text language sql as $$
        select _p.value1::text || ' ' || _p.value2 || ' ' || _p.value3::text;
        $$;
");
    }
}

[Collection("TestFixture")]
public class CustomTableTypeParametersTests(TestFixture test)
{
    [Fact]
    public async Task Test_get_custom_table_param_query_1p()
    {
        var query = new QueryBuilder
        {
            { "i", "1" },
            { "pValue", "test 123" },
            { "t", "test XYZ" },
        };
        using var response = await test.Client.GetAsync($"/api/get-custom-table-param-query-1p/{query}");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("test 123");
    }

    [Fact]
    public async Task Test_get_custom_table_param_query_1p_2()
    {
        var query = new QueryBuilder
        {
            { "pValue", "test 123" },
            { "i", "1" },
            { "t", "test XYZ" },
        };
        using var response = await test.Client.GetAsync($"/api/get-custom-table-param-query-1p/{query}");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("test 123");
    }

    [Fact]
    public async Task Test_get_custom_table_param_query_2p()
    {
        var query = new QueryBuilder
        {
            { "pValue", "test 123" },
        };
        using var response = await test.Client.GetAsync($"/api/get-custom-table-param-query-2p/{query}");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("test 123");
    }

    [Fact]
    public async Task Test_get_custom_table_param_query_3p()
    {
        var query = new QueryBuilder
        {
            { "pValue", "test 123" },
            { "i", "1" },
            { "t", "test XYZ" },
        };
        using var response = await test.Client.GetAsync($"/api/get-custom-table-param-query-3p/{query}");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("test 123");
    }

    [Fact]
    public async Task Test_get_custom_table_param_query_4p()
    {
        var query = new QueryBuilder
        {
            { "pValue1", "1" },
            { "pValue2", "test 123" },
            { "pValue3", "true" },
        };
        using var response = await test.Client.GetAsync($"/api/get-custom-table-param-query-4p/{query}");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("test 123");
    }

    [Fact]
    public async Task Test_get_custom_table_param_query_5p()
    {
        var query = new QueryBuilder
        {
            { "p2Value1", "1" },
            { "p2Value2", "test 123" },
            { "p2Value3", "true" },
            { "p1Value", "test XYZ" },
        };
        using var response = await test.Client.GetAsync($"/api/get-custom-table-param-query-5p/{query}");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("test 123");
    }

    [Fact]
    public async Task Test_get_custom_table_param_query_5p_2()
    {
        var query = new QueryBuilder
        {
            { "p2Value1", "1" },
            { "p1Value", "test XYZ" },
            { "p2Value3", "true" },
            { "p2Value2", "test 123" },
        };
        using var response = await test.Client.GetAsync($"/api/get-custom-table-param-query-5p/{query}");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("test 123");
    }

    [Fact]
    public async Task Test_get_custom_table_param_query_6p()
    {
        var query = new QueryBuilder
        {
            { "p1", "1" },
            { "p2Value", "test XYZ" },
            { "p3Value1", "1" },
            { "p3Value2", "test 123" },
            { "p3Value3", "true" },
            
        };
        using var response = await test.Client.GetAsync($"/api/get-custom-table-param-query-6p/{query}");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("1test 123");
    }

    [Fact]
    public async Task Test_get_custom_table_param_query_6p_2()
    {
        var query = new QueryBuilder
        {
            { "p2Value", "test XYZ" },
            { "p3Value1", "1" },
            { "p1", "1" },
            { "p3Value2", "test 123" },
            { "p3Value3", "true" },

        };
        using var response = await test.Client.GetAsync($"/api/get-custom-table-param-query-6p/{query}");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("1test 123");
    }

    [Fact]
    public async Task Test_get_custom_table_param_query_7p()
    {
        var query = new QueryBuilder
        {
            { "p1", "1" },
            { "p2Value", "test XYZ" },
            { "p3Value1", "1" },
            { "p3Value2", "test 123" },
            { "p3Value3", "true" },

        };
        using var response = await test.Client.GetAsync($"/api/get-custom-table-param-query-7p/{query}");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("1test 123");
    }

    [Fact]
    public async Task Test_get_custom_table_param_query_8p()
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
        using var response = await test.Client.GetAsync($"/api/get-custom-table-param-query-8p/{query}");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("1test 123");
    }

    [Fact]
    public async Task Test_get_custom_table_param_query_9p()
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
        using var response = await test.Client.GetAsync($"/api/get-custom-table-param-query-9p/{query}");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("1test 123");
    }

    [Fact]
    public async Task Test_custom_table_param_schema_custom_table_type()
    {
        var query = new QueryBuilder
        {
            { "pValue1", "1" },
            { "pValue2", "test" },
            { "pValue3", "true" },
        };
        using var response = await test.Client.GetAsync($"/api/custom-table-param-schema/get-custom-table-type/{query}");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("1 test true");
    }
}