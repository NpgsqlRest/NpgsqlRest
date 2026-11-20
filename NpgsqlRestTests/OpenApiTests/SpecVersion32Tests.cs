using System.Text.Json.Nodes;
using NpgsqlRest.OpenAPI;

namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void OpenApiSpecVersion32Tests()
        {
            script.Append("""
create function openapi_q32_body(_term text, _limit int default 10)
returns table (id int, name text)
language sql as $$ select 1, _term $$;
comment on function openapi_q32_body(text, int) is '
Search by a term.
The term travels in the request body.
HTTP QUERY
';

create function openapi_q32_no_params() returns text language sql as $$ select 'x' $$;
comment on function openapi_q32_no_params() is 'HTTP QUERY';

create function openapi_q32_in_path(_category int, _term text) returns text language sql as $$ select _term $$;
comment on function openapi_q32_in_path(int, text) is '
HTTP QUERY /api/openapi-q32/{_category}/search
';

create function openapi_q32_query_string(_term text) returns text language sql as $$ select _term $$;
comment on function openapi_q32_query_string(text) is '
HTTP QUERY
param_type query_string
';

create function openapi_q32_shared_get() returns text language sql as $$ select 'get' $$;
comment on function openapi_q32_shared_get() is 'HTTP GET /api/openapi-q32/shared';
create function openapi_q32_shared_query(_term text) returns text language sql as $$ select _term $$;
comment on function openapi_q32_shared_query(text) is 'HTTP QUERY /api/openapi-q32/shared';

create function openapi_q32_hidden(_term text) returns text language sql as $$ select _term $$;
comment on function openapi_q32_hidden(text) is '
HTTP QUERY
openapi hide
';

create function openapi_q32_tagged(_term text) returns text language sql as $$ select _term $$;
comment on function openapi_q32_tagged(text) is '
HTTP QUERY
openapi tags Partner API, Search
';
""");
        }
    }
}

namespace NpgsqlRestTests.OpenApiTests
{
    /// <summary>
    /// SpecVersion "3.2": the document says openapi 3.2.0 and documents endpoints of the HTTP QUERY method under
    /// the "query" key of their path, which OpenAPI 3.0 and 3.1 have no place for.
    /// </summary>
    [Collection("TestFixture")]
    public class SpecVersion32Tests(Setup.TestFixture test)
    {
        private static JsonNode Load(string outputPath)
        {
            var filePath = Path.Combine(outputPath, "openapi.json");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");
            return JsonNode.Parse(File.ReadAllText(filePath))!;
        }

        private static JsonNode Doc30 => Load(Setup.Program.OpenApiOutputPath);
        private static JsonNode Doc31 => Load(Setup.Program.OpenApi31OutputPath);
        private static JsonNode Doc32 => Load(Setup.Program.OpenApi32OutputPath);

        [Fact]
        public void Document_says_openapi_3_2_0()
        {
            OpenApiSpecVersions.V32.Should().Be("3.2.0");
            Doc32["openapi"]!.GetValue<string>().Should().Be("3.2.0");
        }

        [Theory]
        [InlineData("3.2")]
        [InlineData(" 3.2 ")]
        [InlineData("3.2\t")]
        public void Spec_version_3_2_is_accepted(string value)
        {
            var act = () => new OpenApi(new OpenApiOptions { SpecVersion = value });
            act.Should().NotThrow();
        }

        [Theory]
        [InlineData("3.2.0")]
        [InlineData("3.3")]
        [InlineData("32")]
        [InlineData("")]
        [InlineData(null)]
        public void Another_value_fails_and_the_message_lists_all_three(string? value)
        {
            var act = () => new OpenApi(new OpenApiOptions { SpecVersion = value! });
            act.Should().Throw<ArgumentException>().WithMessage(
                $"Invalid OpenApiOptions.SpecVersion value '{value}'. Valid values are \"3.0\" (emits openapi: 3.0.3), \"3.1\" (emits openapi: 3.1.1) and \"3.2\" (emits openapi: 3.2.0).");
        }

        [Fact]
        public void Without_the_query_operations_the_document_is_the_3_0_document()
        {
            var doc30 = Doc30;
            var doc32 = Doc32;
            doc30["openapi"] = "normalized";
            doc32["openapi"] = "normalized";

            var paths = doc32["paths"]!.AsObject();
            var removed = 0;
            foreach (var (path, item) in paths.ToList())
            {
                if (item!.AsObject().Remove("query"))
                {
                    removed++;
                }
                if (item.AsObject().Count == 0)
                {
                    paths.Remove(path);
                }
            }
            removed.Should().BeGreaterThan(5);
            doc32.ToJsonString().Should().Be(doc30.ToJsonString());
        }

        [Theory]
        [InlineData("/api/openapi-q32-body")]
        [InlineData("/api/openapi-q32-no-params")]
        [InlineData("/api/openapi-q32/{_category}/search")]
        [InlineData("/api/openapi-q32-query-string")]
        [InlineData("/api/openapi-q32-tagged")]
        [InlineData("/api/tsclient-test/query-search")]
        public void Earlier_versions_leave_query_endpoints_out(string path)
        {
            Doc30["paths"]!.AsObject().ContainsKey(path).Should().BeFalse();
            Doc31["paths"]!.AsObject().ContainsKey(path).Should().BeFalse();
            Doc32["paths"]![path]!["query"].Should().NotBeNull();
        }

        [Fact]
        public void Query_operation_with_its_parameters_in_the_body()
        {
            Doc32["paths"]!["/api/openapi-q32-body"]!.ToJsonString().Should().Be(
                """{"query":{"summary":"Search by a term.","description":"The term travels in the request body.","tags":["public"],"operationId":"public_openapi_q32_body_query","requestBody":{"required":true,"content":{"application/json":{"schema":{"type":"object","properties":{"term":{"type":"string"},"limit":{"type":"integer","format":"int32"}},"required":["term"]}}}},"responses":{"200":{"description":"Successful response","content":{"application/json":{"schema":{"type":"array","items":{"type":"object","properties":{"id":{"type":"integer","format":"int32"},"name":{"type":"string"}}}}}}}}}}""");
        }

        [Fact]
        public void Query_operation_without_parameters()
        {
            Doc32["paths"]!["/api/openapi-q32-no-params"]!.ToJsonString().Should().Be(
                """{"query":{"summary":"Function public.openapi_q32_no_params","tags":["public"],"operationId":"public_openapi_q32_no_params_query","responses":{"200":{"description":"Successful response","content":{"application/json":{"schema":{"type":"string"}}}}}}}""");
        }

        [Fact]
        public void Query_operation_with_a_path_parameter()
        {
            Doc32["paths"]!["/api/openapi-q32/{_category}/search"]!.ToJsonString().Should().Be(
                """{"query":{"summary":"Function public.openapi_q32_in_path","tags":["public"],"operationId":"public_openapi_q32_in_path_query","requestBody":{"required":true,"content":{"application/json":{"schema":{"type":"object","properties":{"term":{"type":"string"}},"required":["term"]}}}},"parameters":[{"name":"category","in":"path","required":true,"schema":{"type":"integer","format":"int32"}}],"responses":{"200":{"description":"Successful response","content":{"application/json":{"schema":{"type":"string"}}}}}}}""");
        }

        [Fact]
        public void Query_operation_with_parameters_in_the_query_string()
        {
            Doc32["paths"]!["/api/openapi-q32-query-string"]!.ToJsonString().Should().Be(
                """{"query":{"summary":"Function public.openapi_q32_query_string","tags":["public"],"operationId":"public_openapi_q32_query_string_query","parameters":[{"name":"term","in":"query","required":true,"schema":{"type":"string"}}],"responses":{"200":{"description":"Successful response","content":{"application/json":{"schema":{"type":"string"}}}}}}}""");
        }

        [Fact]
        public void Query_and_get_share_a_path()
        {
            var item = Doc32["paths"]!["/api/openapi-q32/shared"]!.AsObject();
            item.Select(p => p.Key).Should().BeEquivalentTo(["get", "query"]);
            item["get"]!["operationId"]!.GetValue<string>().Should().Be("public_openapi_q32_shared_get_get");
            item["query"]!["operationId"]!.GetValue<string>().Should().Be("public_openapi_q32_shared_query_query");

            // in the earlier versions the path is there with its GET only
            Doc30["paths"]!["/api/openapi-q32/shared"]!.AsObject().Select(p => p.Key).Should().BeEquivalentTo(["get"]);
            Doc31["paths"]!["/api/openapi-q32/shared"]!.AsObject().Select(p => p.Key).Should().BeEquivalentTo(["get"]);
        }

        [Fact]
        public void A_hidden_query_endpoint_stays_hidden()
        {
            Doc32["paths"]!.AsObject().ContainsKey("/api/openapi-q32-hidden").Should().BeFalse();
        }

        [Fact]
        public void Tags_apply_to_a_query_operation()
        {
            Doc32["paths"]!["/api/openapi-q32-tagged"]!["query"]!["tags"]!.ToJsonString().Should().Be("[\"Partner API\",\"Search\"]");
        }

        [Fact]
        public async Task The_documented_endpoints_answer_to_the_query_method()
        {
            using var body = new HttpRequestMessage(new HttpMethod("QUERY"), "/api/openapi-q32-body")
            {
                Content = new StringContent("""{"term":"abc"}""", Encoding.UTF8, "application/json")
            };
            using var bodyResponse = await test.Client.SendAsync(body);
            bodyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            (await bodyResponse.Content.ReadAsStringAsync()).Should().Be("""[{"id":1,"name":"abc"}]""");

            using var path = new HttpRequestMessage(new HttpMethod("QUERY"), "/api/openapi-q32/7/search")
            {
                Content = new StringContent("""{"term":"in path"}""", Encoding.UTF8, "application/json")
            };
            using var pathResponse = await test.Client.SendAsync(path);
            pathResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            (await pathResponse.Content.ReadAsStringAsync()).Should().Be("in path");

            using var query = new HttpRequestMessage(new HttpMethod("QUERY"), "/api/openapi-q32-query-string?term=qs");
            using var queryResponse = await test.Client.SendAsync(query);
            queryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            (await queryResponse.Content.ReadAsStringAsync()).Should().Be("qs");

            using var shared = new HttpRequestMessage(new HttpMethod("QUERY"), "/api/openapi-q32/shared")
            {
                Content = new StringContent("""{"term":"shared"}""", Encoding.UTF8, "application/json")
            };
            using var sharedResponse = await test.Client.SendAsync(shared);
            (await sharedResponse.Content.ReadAsStringAsync()).Should().Be("shared");
            (await test.Client.GetStringAsync("/api/openapi-q32/shared")).Should().Be("get");
        }
    }
}
