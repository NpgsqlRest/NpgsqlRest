using NpgsqlRest.OpenAPI;

namespace NpgsqlRestTests.OpenApiTests
{
    [Collection("TestFixture")]
    public class SpecVersionTests
    {
        private static JsonNode LoadDocument(string outputPath)
        {
            var filePath = Path.Combine(outputPath, "openapi.json");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var document = JsonNode.Parse(File.ReadAllText(filePath));
            document.Should().NotBeNull();
            return document!;
        }

        /// <summary>
        /// SpecVersion "3.1" emits openapi: 3.1.1, and — since the emitter uses no keyword whose
        /// semantics differ between OpenAPI 3.0 and 3.1 — the rest of the document is identical
        /// to the 3.0 output for the same endpoint set.
        /// </summary>
        [Fact]
        public void Test_SpecVersion31_EmitsV31_AndDocumentOtherwiseIdenticalTo30()
        {
            var doc30 = LoadDocument(Setup.Program.OpenApiOutputPath);
            var doc31 = LoadDocument(Setup.Program.OpenApi31OutputPath);

            doc30["openapi"]?.GetValue<string>().Should().Be(OpenApiSpecVersions.V30);
            doc31["openapi"]?.GetValue<string>().Should().Be(OpenApiSpecVersions.V31);

            // Normalize the spec version field and compare the rest of the documents.
            doc30["openapi"] = "normalized";
            doc31["openapi"] = "normalized";
            doc31.ToJsonString().Should().Be(doc30.ToJsonString(),
                "apart from the openapi field, the 3.1 document must be identical to the 3.0 document");
        }

        /// <summary>
        /// An unsupported SpecVersion value must fail fast when the handler is constructed
        /// (startup time), with a message listing the valid values.
        /// </summary>
        [Fact]
        public void Test_InvalidSpecVersion_FailsFastAtConstruction()
        {
            var act = () => new OpenApi(new OpenApiOptions { SpecVersion = "2.0" });

            act.Should().Throw<ArgumentException>()
                .WithMessage("*Invalid OpenApiOptions.SpecVersion value '2.0'*")
                .WithMessage("*\"3.0\"*3.0.3*")
                .WithMessage("*\"3.1\"*3.1.1*");
        }

        /// <summary>
        /// Accepted values are trimmed and compared case-insensitively; both valid versions resolve
        /// without throwing.
        /// </summary>
        [Fact]
        public void Test_ValidSpecVersions_AreAccepted()
        {
            new OpenApi(new OpenApiOptions { SpecVersion = "3.0" }).Should().NotBeNull();
            new OpenApi(new OpenApiOptions { SpecVersion = "3.1" }).Should().NotBeNull();
            new OpenApi(new OpenApiOptions { SpecVersion = " 3.1 " }).Should().NotBeNull();
        }
    }
}
