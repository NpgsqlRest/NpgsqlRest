namespace NpgsqlRestTests
{
    public static partial class Database
    {
        /// <summary>
        /// SQL setup for the OpenAPI comment-annotation tests. Each function exercises a different
        /// shape of the new <c>openapi</c> annotation: bare hide, explicit hide aliases, tag override
        /// with a single value, tag override with multiple values, plus a control case that uses no
        /// annotation (schema-default tag).
        ///
        /// These functions ride along on the global TestFixture's OpenAPI document, so the tests just
        /// read the generated <c>openapi.json</c> and assert against expected presence / shape — no
        /// custom NpgsqlRest pipeline is built.
        /// </summary>
        public static void OpenApiAnnotationTests()
        {
            script.Append("""

            create function openapi_annot_visible(_x int)
            returns int
            language sql as $$
            select _x;
            $$;
            comment on function openapi_annot_visible(int) is '
            HTTP GET /api/openapi-annot-visible
            ';

            create function openapi_annot_hidden_bare(_x int)
            returns int
            language sql as $$
            select _x;
            $$;
            comment on function openapi_annot_hidden_bare(int) is '
            HTTP GET /api/openapi-annot-hidden-bare
            openapi
            ';

            create function openapi_annot_hidden_explicit(_x int)
            returns int
            language sql as $$
            select _x;
            $$;
            comment on function openapi_annot_hidden_explicit(int) is '
            HTTP GET /api/openapi-annot-hidden-explicit
            openapi hide
            ';

            create function openapi_annot_hidden_alias_hidden(_x int)
            returns int
            language sql as $$
            select _x;
            $$;
            comment on function openapi_annot_hidden_alias_hidden(int) is '
            HTTP GET /api/openapi-annot-hidden-alias-hidden
            openapi hidden
            ';

            create function openapi_annot_hidden_alias_ignore(_x int)
            returns int
            language sql as $$
            select _x;
            $$;
            comment on function openapi_annot_hidden_alias_ignore(int) is '
            HTTP GET /api/openapi-annot-hidden-alias-ignore
            openapi ignore
            ';

            create function openapi_annot_tag_single(_x int)
            returns int
            language sql as $$
            select _x;
            $$;
            comment on function openapi_annot_tag_single(int) is '
            HTTP GET /api/openapi-annot-tag-single
            openapi tag partner
            ';

            create function openapi_annot_tags_multi(_x int)
            returns int
            language sql as $$
            select _x;
            $$;
            comment on function openapi_annot_tags_multi(int) is '
            HTTP GET /api/openapi-annot-tags-multi
            openapi tags partner, billing
            ';

            create function openapi_annot_tag_preserves_case(_x int)
            returns int
            language sql as $$
            select _x;
            $$;
            comment on function openapi_annot_tag_preserves_case(int) is '
            HTTP GET /api/openapi-annot-tag-preserves-case
            openapi tag PartnerAPI
            ';
""");
        }
    }
}

namespace NpgsqlRestTests.OpenApiTests
{
    /// <summary>
    /// End-to-end tests for the <c>openapi</c> comment annotation. The global TestFixture's OpenAPI
    /// handler ingests every routine including the ones declared in
    /// <see cref="Database.OpenApiAnnotationTests"/>, and writes the result to
    /// <see cref="Setup.Program.OpenApiOutputPath"/>. Tests read that JSON and verify each routine's
    /// presence (or absence) and its tags.
    ///
    /// What these tests prove:
    ///   - The annotation parser routes <c>openapi hide / hidden / ignore / (bare)</c> to
    ///     <c>RoutineEndpoint.OpenApiHide = true</c>.
    ///   - The plugin's <see cref="OpenApi.Handle"/> filter gate skips hidden endpoints — the HTTP
    ///     route is still registered (existing assertions about the path being reachable would still
    ///     pass) but the openapi.json contains no entry for it.
    ///   - <c>openapi tag &lt;name&gt;</c> replaces the default schema-based tag with the supplied
    ///     value(s), preserving original casing for tools that render group headings verbatim.
    /// </summary>
    [Collection("TestFixture")]
    public class OpenApiAnnotationTests
    {
        private static JsonNode GetDocument()
        {
            var filePath = Path.Combine(Setup.Program.OpenApiOutputPath, "openapi.json");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");
            var content = File.ReadAllText(filePath);
            var doc = JsonNode.Parse(content);
            doc.Should().NotBeNull();
            return doc!;
        }

        [Fact]
        public void Annot_visible_function_appears_in_document()
        {
            // Control case — no openapi annotation, must be documented as normal so the negative cases
            // below carry weight.
            var doc = GetDocument();

            doc["paths"]?["/api/openapi-annot-visible"].Should().NotBeNull(
                "the control function with no openapi annotation must be in the document");
        }

        [Theory]
        [InlineData("/api/openapi-annot-hidden-bare")]
        [InlineData("/api/openapi-annot-hidden-explicit")]
        [InlineData("/api/openapi-annot-hidden-alias-hidden")]
        [InlineData("/api/openapi-annot-hidden-alias-ignore")]
        public void Annot_hidden_routines_are_absent_from_document(string path)
        {
            // Every form of the hide annotation — bare `openapi`, `hide`, `hidden`, `ignore` — must
            // result in the path being absent from the generated spec while the HTTP endpoint itself
            // remains functional (the endpoint registration is unaffected by the plugin filter).
            var doc = GetDocument();

            doc["paths"]?[path].Should().BeNull(
                $"endpoint with `openapi hide`-style annotation must not appear in the openapi.json " +
                $"(path: {path})");
        }

        [Fact]
        public void Annot_tag_single_replaces_default_schema_tag()
        {
            var doc = GetDocument();

            var tags = doc["paths"]?["/api/openapi-annot-tag-single"]?["get"]?["tags"]?.AsArray();
            tags.Should().NotBeNull("path must exist in the document");
            tags!.Count.Should().Be(1, "single tag annotation should produce one tag");
            tags[0]!.GetValue<string>().Should().Be("partner",
                "annotation `openapi tag partner` must replace the default schema-based tag");
        }

        [Fact]
        public void Annot_tags_multi_emits_all_supplied_tags()
        {
            var doc = GetDocument();

            var tags = doc["paths"]?["/api/openapi-annot-tags-multi"]?["get"]?["tags"]?.AsArray();
            tags.Should().NotBeNull("path must exist in the document");
            tags!.Count.Should().Be(2, "two comma-separated tags must produce two entries");

            var values = tags!.Select(t => t!.GetValue<string>()).ToArray();
            values.Should().BeEquivalentTo(new[] { "partner", "billing" });
        }

        [Fact]
        public void Annot_tag_preserves_original_casing()
        {
            // The comment-parser pipeline lowercases its primary token stream (so `OPENAPI TAG` works)
            // but tag values are read from the original casing word stream so display in Swagger UI
            // / ReDoc matches what the author wrote.
            var doc = GetDocument();

            var tags = doc["paths"]?["/api/openapi-annot-tag-preserves-case"]?["get"]?["tags"]?.AsArray();
            tags.Should().NotBeNull();
            tags![0]!.GetValue<string>().Should().Be("PartnerAPI",
                "tag values must preserve original casing for display in Swagger UI / ReDoc");
        }

        [Fact]
        public void Annot_default_tag_is_routine_schema_when_no_openapi_tag()
        {
            // Sanity check: control case still gets the default schema tag (this is the existing
            // behavior, just verifying it's not been broken by the new tag override path).
            var doc = GetDocument();

            var tags = doc["paths"]?["/api/openapi-annot-visible"]?["get"]?["tags"]?.AsArray();
            tags.Should().NotBeNull();
            tags!.Count.Should().Be(1);
            tags[0]!.GetValue<string>().Should().Be("public",
                "without `openapi tag` the default tag is the routine's schema (here: public)");
        }
    }
}
