namespace NpgsqlRestTests.HttpFilesTests
{
    // Exercises HttpFileOptions.OmitAutomaticParameters = true (the Setup.Program "HttpFilesOmit" config).
    // Reuses the tsclient_test.bodyparam_* functions (HTTP Custom Type parameters). Server-filled
    // parameters that cannot be set by the client are dropped from the generated request.
    [Collection("TestFixture")]
    public class OmitAutomaticParamsTests
    {
        private static string ReadOmitFile()
        {
            var filePath = Path.Combine(Setup.Program.HttpFilesOmitOutputPath, "npgsqlrest.http");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");
            return File.ReadAllText(filePath);
        }

        // GET with a normal parameter (keyword) + an HTTP Custom Type: only keyword stays on the query.
        [Fact]
        public void Test_MixedParams_OnlyNonAutomaticOnQuery()
        {
            var section = ExtractSection(ReadOmitFile(), "/api/tsclient-test/bodyparam-mixed");
            section.Should().NotBeNull("Should find the mixed endpoint");

            section.Should().Contain("keyword=", "the non-automatic parameter stays on the query string");
            section.Should().NotContain("responseBody=", "HTTP Custom Type fields are omitted");
            section.Should().NotContain("responseStatusCode=", "HTTP Custom Type fields are omitted");
            section.Should().NotContain("responseSuccess=", "HTTP Custom Type fields are omitted");
        }

        // POST + @body_parameter_name targeting an HTTP Custom Type field: every parameter is automatic,
        // so the request is just the bare URL — no query string and no body.
        [Fact]
        public void Test_AllAutomatic_BareRequest()
        {
            var section = ExtractSection(ReadOmitFile(), "/api/tsclient-test/bodyparam-expanded");
            section.Should().NotBeNull("Should find the expanded endpoint");

            section.Should().NotContain("responseBody", "HTTP Custom Type fields are omitted");
            section.Should().NotContain("responseStatusCode", "HTTP Custom Type fields are omitted");
            section.Should().NotContain("?", "no query string when every parameter is omitted");
            section.Should().NotContain("content-type: application/json", "no body when the body parameter is omitted");
        }

        // Mirrors PathParameterTests.ExtractSection.
        private static string? ExtractSection(string content, string marker)
        {
            var startIndex = content.IndexOf(marker, StringComparison.Ordinal);
            if (startIndex < 0)
            {
                return null;
            }
            var blockStart = content.LastIndexOf("###", startIndex, StringComparison.Ordinal);
            if (blockStart < 0)
            {
                blockStart = content.LastIndexOf("@host=", startIndex, StringComparison.Ordinal);
                if (blockStart < 0)
                {
                    blockStart = 0;
                }
                else
                {
                    var nextNewline = content.IndexOf('\n', blockStart);
                    if (nextNewline >= 0)
                    {
                        blockStart = nextNewline + 1;
                    }
                }
            }
            else
            {
                blockStart += 3;
            }
            var endIndex = content.IndexOf("###", startIndex, StringComparison.Ordinal);
            if (endIndex < 0)
            {
                endIndex = content.Length;
            }
            return content.Substring(blockStart, endIndex - blockStart).Trim();
        }
    }
}
