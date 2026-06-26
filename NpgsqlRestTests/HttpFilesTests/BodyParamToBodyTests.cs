namespace NpgsqlRestTests.HttpFilesTests
{
    // Regression: @body_parameter_name targeting an HTTP Custom Type field by its expanded name
    // (_response_body) must move that field into the request BODY, not leave it in the query string.
    // Reads the default (omission-off) HTTP file. Reuses tsclient_test.bodyparam_expanded.
    [Collection("TestFixture")]
    public class BodyParamToBodyTests
    {
        [Fact]
        public void Test_ExpandedBodyParam_GoesToBodyNotQuery()
        {
            var filePath = Path.Combine(Setup.Program.HttpFilesOutputPath, "npgsqlrest.http");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var section = ExtractSection(File.ReadAllText(filePath), "/api/tsclient-test/bodyparam-expanded");
            section.Should().NotBeNull("Should find the expanded endpoint");

            var urlLine = section!.Split('\n')[0];
            // The body parameter must NOT be on the query string …
            urlLine.Should().NotContain("responseBody=", "the body parameter belongs in the body, not the query");
            // … while the remaining small fields still travel on the query.
            urlLine.Should().Contain("responseStatusCode=", "non-body fields stay on the query string");
            // … and the body value is emitted after the request line.
            var afterUrl = section[(section.IndexOf('\n') + 1)..].Trim();
            afterUrl.Should().NotBeEmpty("the body parameter value must be emitted as the request body");
        }

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
