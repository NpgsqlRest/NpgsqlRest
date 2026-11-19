using System.Text.Json.Nodes;

namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void OpenApiCommentDisclosureTests()
        {
            script.Append("""
create function openapi_disclosure_get()
returns text language sql as $$ select 'ok' $$;
comment on function openapi_disclosure_get() is '
Returns the current status.
Used by the dashboard.
HTTP GET
basic_auth admin S3cretPassw0rd
proxy_host = http://internal-billing:8080
';
""");
        }
    }
}

namespace NpgsqlRestTests.OpenApiTests
{
    /// <summary>
    /// Security regression: the OpenAPI document takes an operation's summary and description from the comment's prose
    /// lines only. It used to publish the raw comment, annotations included - e.g. basic_auth credentials and internal
    /// hosts - in a document that is typically public.
    /// </summary>
    [Collection("TestFixture")]
    public class OpenApiCommentDisclosureTests
    {
        [Fact]
        public void Annotation_lines_are_not_published()
        {
            var doc = JsonNode.Parse(File.ReadAllText(Path.Combine(Setup.Program.OpenApiOutputPath, "openapi.json")))!;
            var operation = doc["paths"]?["/api/openapi-disclosure-get"]?["get"];
            operation.Should().NotBeNull();

            operation!["summary"]!.GetValue<string>().Should().Be("Returns the current status.");
            operation["description"]!.GetValue<string>().Should().Be("Used by the dashboard.");
            operation.ToJsonString().Should().NotContain("S3cretPassw0rd").And.NotContain("internal-billing");
        }
    }
}
