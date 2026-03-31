namespace NpgsqlRestTests.TsClientTests
{
    [Collection("TestFixture")]
    public class SkipTypesTests
    {
        // greet_status uses tsclient_status_code=true - verify no TypeScript-only "as" cast in JS output
        [Fact]
        public void Test_GreetStatus_SkipTypes_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientJsOutputPath, "greet_status.js");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);

            // Must NOT contain TypeScript "as" type cast - that's invalid JavaScript
            content.Should().NotContain(" as {");
            content.Should().NotContain(" as ApiError");

            // Must NOT contain TypeScript type annotations on function signature
            content.Should().NotContain(": Promise<");
            content.Should().NotContain("request: I");

            // Must NOT contain type alias definitions
            content.Should().NotContain("type ApiError");
            content.Should().NotContain("type ApiResult");

            // Should still contain the error handling logic (just without "as" cast)
            content.Should().Contain("await response.json() : undefined");
            content.Should().Contain("status: response.status");
        }

        // do_nothing_status returns void with tsclient_status_code=true - verify no "as" cast in JS output
        [Fact]
        public void Test_DoNothingStatus_SkipTypes_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientJsOutputPath, "do_nothing_status.js");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);

            // Must NOT contain TypeScript "as" type cast
            content.Should().NotContain(" as {");
            content.Should().NotContain(" as ApiError");

            // Must NOT contain type annotations or aliases
            content.Should().NotContain(": Promise<");
            content.Should().NotContain("type ApiError");

            // Should contain void status handling
            content.Should().Contain("await response.json() : undefined");
            content.Should().Contain("status: response.status");
        }

        // greet (no status code) - verify basic JS output works
        [Fact]
        public void Test_Greet_SkipTypes_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientJsOutputPath, "greet.js");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);

            // Must NOT contain TypeScript syntax on function signature
            content.Should().NotContain(": Promise<");
            content.Should().NotContain("request: I");
            content.Should().NotContain("type ApiError");

            // Should contain the function
            content.Should().Contain("export async function tsclientTestGreet(");
            content.Should().Contain("return await response.text();");
        }
    }
}
