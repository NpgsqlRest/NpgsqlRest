using System.Text.Json.Nodes;

namespace NpgsqlRestTests.OpenApiTests
{
    // Exercises OpenApiOptions.OmitAutomaticParameters = true (the Setup.Program "OpenApiOmit" config).
    // Reuses the tsclient_test.bodyparam_* functions (HTTP Custom Type parameters). Server-filled
    // parameters that cannot be set by the client are dropped from documented query parameters / body.
    [Collection("TestFixture")]
    public class OmitAutomaticParamsTests
    {
        private static JsonNode GetOmitDocument()
        {
            var filePath = Path.Combine(Setup.Program.OpenApiOmitOutputPath, "openapi.json");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");
            var doc = JsonNode.Parse(File.ReadAllText(filePath));
            doc.Should().NotBeNull();
            return doc!;
        }

        // GET with a normal parameter (keyword) + an HTTP Custom Type: only keyword is documented.
        [Fact]
        public void Test_MixedParams_OnlyNonAutomaticDocumented()
        {
            var doc = GetOmitDocument();
            var parameters = doc["paths"]?["/api/tsclient-test/bodyparam-mixed"]?["get"]?["parameters"] as JsonArray;
            parameters.Should().NotBeNull("the GET endpoint should have query parameters documented");

            var names = parameters!.Select(p => p?["name"]?.GetValue<string>()).ToList();
            names.Should().Contain("keyword", "the non-automatic parameter is documented");
            names.Should().NotContain("responseBody", "HTTP Custom Type fields are omitted");
            names.Should().NotContain("responseStatusCode", "HTTP Custom Type fields are omitted");
            names.Should().NotContain("responseSuccess", "HTTP Custom Type fields are omitted");
            names.Should().NotContain("responseErrorMessage", "HTTP Custom Type fields are omitted");
        }

        // POST + @body_parameter_name targeting an HTTP Custom Type field: every parameter is automatic,
        // so the operation has no documented query parameters and no request body.
        [Fact]
        public void Test_AllAutomatic_NoParametersNoBody()
        {
            var doc = GetOmitDocument();
            var operation = doc["paths"]?["/api/tsclient-test/bodyparam-expanded"]?["post"];
            operation.Should().NotBeNull("the POST endpoint should be documented");

            var parameters = operation!["parameters"] as JsonArray;
            (parameters is null || parameters.Count == 0).Should().BeTrue("no query parameters when every parameter is omitted");
            operation["requestBody"].Should().BeNull("no request body when the body parameter is omitted");
        }
    }
}
