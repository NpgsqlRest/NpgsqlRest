#pragma warning disable CS8602 // Dereference of a possibly null reference.
namespace NpgsqlRestTests;

public static partial class Database
{
    public static void MultiParamsQueryStringTests2()
    {
        script.Append(@"
create function case_get_multi_params2(
    _real real,
    _double double precision,
    _jsonpath jsonpath,
    _timestamp timestamp,
    _timestamptz timestamptz,
    _date date,
    _time time,
    _timetz timetz,
    _interval interval,
    _bool bool,
    _uuid uuid,
    _varbit varbit,
    _bit bit(3),
    _inet inet,
    _macaddr macaddr,
    _bytea bytea
) 
returns json
language plpgsql
as 
$$
begin
    return json_build_object(
        'real', _real,
        'double', _double,
        'jsonpath', _jsonpath,
        'timestamp', _timestamp,
        'timestamptz', _timestamptz,
        'date', _date,
        'time', _time,
        'timetz', _timetz,
        'interval', _interval,
        'bool', _bool,
        'uuid', _uuid,
        'varbit', _varbit,
        'bit', _bit,
        'inet', _inet,
        'macaddr', _macaddr,
        'bytea', _bytea
    );
end;
$$;
");
    }
}

[Collection("TestFixture")]
public class MultiParamsQueryStringTests2(TestFixture test)
{
    [Fact]
    public async Task Test_case_get_multi_params2()
    {
        var query = new QueryBuilder
        {
            { "real", "1.1" },
            { "double", "2.2" },
            { "jsonpath", "$.user.addresses[0].city" },
            { "timestamp", "2024-01-12 11:59:17.811872" },
            { "timestamptz", "2024-01-12 12:06:59.334476+01" },
            { "date", "2024-01-12" },
            { "time", "12:07:26.933545" },
            { "timetz", "12:07:44.422546+01:00" },
            { "interval", "3 hours 20 minutes" },
            { "bool", "true" },
            { "uuid", "3237788C-F1BA-4379-8F5A-F530CACDE399" },
            { "varbit", "101011100" },
            { "bit", "101" },
            { "inet", "192.168.5.18" },
            { "macaddr", "00-B0-D0-63-C2-26" },
            { "bytea", "\\xDEADBEEF" },
        };

        using var result = await test.Client.GetAsync($"/api/case-get-multi-params2/{query}");
        var response = await result.Content.ReadAsStringAsync();

        result?.StatusCode.Should().Be(HttpStatusCode.OK);
        result?.Content?.Headers?.ContentType?.MediaType.Should().Be("application/json");

        var node = JsonNode.Parse(response);
        node["real"].ToJsonString().Should().Be("1.1");
        node["double"].ToJsonString().Should().Be("2.2");
        node["jsonpath"].GetValue<string>().Should().Be("$.\"user\".\"addresses\"[0].\"city\"");
        node["timestamp"].GetValue<string>().Should().Be("2024-01-12T11:59:17.811872");

        // integration server seems to have a different datetime alltogether
        node["timestamptz"].GetValue<string>().Should()
            .Match(t => t == "2024-01-12T12:06:59.334476+00:00" || t == "2024-01-12T11:06:59.334476+00:00");
        
        node["date"].GetValue<string>().Should().Be("2024-01-12");
        node["time"].GetValue<string>().Should().Be("12:07:26.933545");

        // integration server seems to have a different datetime alltogether
        node["timetz"].GetValue<string>().Should()
            .Match(t => t == "12:07:44.422546+00" || t == "11:07:44.422546+00" || t == "13:07:44.422546+00");

        node["interval"].GetValue<string>().Should().Be("03:20:00");
        node["bool"].ToJsonString().Should().Be("true");
        node["uuid"].GetValue<string>().Should().Be("3237788c-f1ba-4379-8f5a-f530cacde399");
        node["varbit"].GetValue<string>().Should().Be("101011100");
        node["bit"].GetValue<string>().Should().Be("101");
        node["inet"].GetValue<string>().Should().Be("192.168.5.18");
        node["macaddr"].GetValue<string>().Should().Be("00:b0:d0:63:c2:26");
        node["bytea"].GetValue<string>().Should().Be("\\xdeadbeef");
    }
}