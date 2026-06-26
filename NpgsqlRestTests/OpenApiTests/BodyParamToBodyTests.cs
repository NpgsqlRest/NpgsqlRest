using System.Text.Json.Nodes;

namespace NpgsqlRestTests.OpenApiTests
{
    // Regression: @body_parameter_name targeting an HTTP Custom Type field by its expanded name
    // (_response_body) must document that field as the request body, not as a query parameter.
    // Reads the default (omission-off) document. Reuses tsclient_test.bodyparam_expanded.
    [Collection("TestFixture")]
    public class BodyParamToBodyTests
    {
        [Fact]
        public void Test_ExpandedBodyParam_DocumentedAsBodyNotQuery()
        {
            var filePath = Path.Combine(Setup.Program.OpenApiOutputPath, "openapi.json");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");
            var doc = JsonNode.Parse(File.ReadAllText(filePath))!;

            var operation = doc["paths"]?["/api/tsclient-test/bodyparam-expanded"]?["post"];
            operation.Should().NotBeNull("the POST endpoint should be documented");

            var names = (operation!["parameters"] as JsonArray ?? [])
                .Select(p => p?["name"]?.GetValue<string>())
                .ToList();

            names.Should().NotContain("responseBody", "the body parameter must not be a query parameter");
            names.Should().Contain("responseStatusCode", "non-body fields stay as query parameters");
            operation["requestBody"].Should().NotBeNull("the body parameter must be documented as the request body");
        }
    }
}
