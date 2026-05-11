using Microsoft.AspNetCore.Builder;
using NpgsqlRest;
using NpgsqlRest.OpenAPI;

namespace NpgsqlRestTests.OpenApiTests;

/// <summary>
/// Unit tests for the <see cref="NpgsqlRest.OpenAPI.OpenApi"/> plugin's filter gates:
/// <c>IncludeSchemas</c>, <c>ExcludeSchemas</c>, <c>NameSimilarTo</c>, <c>NameNotSimilarTo</c>,
/// <c>RequiresAuthorizationOnly</c>, plus the per-endpoint <c>OpenApiHide</c> flag (set by the
/// <c>openapi hide</c> annotation upstream in the comment parser). Each test drives the plugin
/// directly with synthetic <see cref="Routine"/> / <see cref="RoutineEndpoint"/> instances —
/// no PostgreSQL connection, no NpgsqlRest pipeline — so we can vary the filter config per case and
/// keep the tests deterministic and fast.
///
/// What's verified:
///   - Each filter rejects the right routines and admits the others.
///   - Filters compose (e.g. include + exclude both apply, name pattern + schema list both apply).
///   - PostgreSQL-style SIMILAR TO patterns translate correctly to .NET regex: <c>_</c> ↔ single
///     char, <c>%</c> ↔ any sequence, regex meta-chars like <c>|</c> / <c>(</c>/<c>)</c> pass
///     through, and the match is anchored.
///   - The output is read by deserializing the JSON file the plugin writes during <c>Cleanup</c> —
///     this is the same artifact tools (Swagger UI, ReDoc, code generators) consume, so failures
///     here mean user-visible failures.
/// </summary>
public class OpenApiFilterTests
{
    private static readonly string TempRoot = Path.Combine(Path.GetTempPath(), "NpgsqlRestTests", "OpenApiFilter");

    static OpenApiFilterTests()
    {
        Directory.CreateDirectory(TempRoot);
    }

    /// <summary>
    /// Minimal valid <see cref="Routine"/> for filter testing. The fields the filter gate consults
    /// (<see cref="Routine.Schema"/>, <see cref="Routine.Name"/>) are explicit parameters; the rest
    /// are filled with neutral values that let <see cref="OpenApi.Handle"/> flow through without
    /// throwing for the positive (filter-pass) case.
    /// </summary>
    private static Routine MakeRoutine(string schema, string name)
        => new()
        {
            Type = RoutineType.Function,
            Schema = schema,
            Name = name,
            Comment = null,
            IsStrict = false,
            CrudType = CrudType.Select,
            ReturnsRecordType = false,
            ReturnsSet = false,
            ColumnCount = 0,
            OriginalColumnNames = [],
            ColumnNames = [],
            JsonColumnNames = [],
            ColumnsTypeDescriptor = [],
            ReturnsUnnamedSet = false,
            IsVoid = true,
            ParamCount = 0,
            Parameters = [],
            ParamsHash = [],
            OriginalParamsHash = [],
            Expression = string.Empty,
            FullDefinition = string.Empty,
            SimpleDefinition = string.Empty,
            FormatUrlPattern = null,
            Tags = null,
            EndpointHandler = null,
            Metadata = null,
        };

    private static RoutineEndpoint MakeEndpoint(
        string schema,
        string name,
        string path,
        bool requiresAuthorization = false,
        bool openApiHide = false,
        string[]? openApiTags = null)
    {
        var routine = MakeRoutine(schema, name);
        var endpoint = new RoutineEndpoint(
            routine: routine,
            path: path,
            method: Method.GET,
            requestParamType: RequestParamType.QueryString,
            requiresAuthorization: requiresAuthorization,
            responseContentType: "application/json",
            responseHeaders: [],
            requestHeadersMode: RequestHeadersMode.Ignore,
            requestHeadersParameterName: string.Empty,
            bodyParameterName: null,
            textResponseNullHandling: TextResponseNullHandling.EmptyString,
            queryStringNullHandling: QueryStringNullHandling.EmptyString);
        endpoint.OpenApiHide = openApiHide;
        endpoint.OpenApiTags = openApiTags;
        return endpoint;
    }

    /// <summary>
    /// Drives <see cref="OpenApi"/> with the given options and endpoints, writing to a per-test temp
    /// file. Returns the parsed JSON document for assertion. The <c>UrlPath</c> is left null so the
    /// plugin's <c>Cleanup</c> branch that touches <c>IApplicationBuilder</c> is bypassed — we don't
    /// need a real builder for file-only output.
    /// </summary>
    private static JsonNode RunHandler(OpenApiOptions options, params RoutineEndpoint[] endpoints)
    {
        // Per-test file in the shared temp root. Caller decides FileName, we use a GUID by default
        // to avoid stomping on parallel test runs.
        if (options.FileName is null)
        {
            options.FileName = Path.Combine(TempRoot, $"openapi-{Guid.NewGuid():N}.json");
        }
        options.FileOverwrite = true;
        // UrlPath must be null so Cleanup doesn't try to touch the (null) builder.
        options.UrlPath = null;

        var handler = new OpenApi(options);
        handler.Setup(builder: null!, options: new NpgsqlRestOptions(string.Empty));
        foreach (var ep in endpoints)
        {
            handler.Handle(ep);
        }
        handler.Cleanup();

        var json = File.ReadAllText(options.FileName);
        var doc = JsonNode.Parse(json);
        doc.Should().NotBeNull();
        return doc!;
    }

    private static bool DocHasPath(JsonNode doc, string path)
        => doc["paths"]?[path] is not null;

    // ------------------------------------------------------------------------
    // Per-endpoint OpenApiHide (from `openapi hide` annotation)
    // ------------------------------------------------------------------------

    [Fact]
    public void OpenApiHide_skips_endpoint_from_document()
    {
        var doc = RunHandler(new OpenApiOptions(),
            MakeEndpoint("public", "visible_fn", "/api/visible"),
            MakeEndpoint("public", "hidden_fn", "/api/hidden", openApiHide: true));

        DocHasPath(doc, "/api/visible").Should().BeTrue("non-hidden endpoint must appear");
        DocHasPath(doc, "/api/hidden").Should().BeFalse("OpenApiHide=true must exclude the endpoint");
    }

    // ------------------------------------------------------------------------
    // RequiresAuthorizationOnly
    // ------------------------------------------------------------------------

    [Fact]
    public void RequiresAuthorizationOnly_excludes_anonymous_endpoints()
    {
        // The partner-document use case: omit the anonymous health / login / probe surface.
        var doc = RunHandler(
            new OpenApiOptions { RequiresAuthorizationOnly = true },
            MakeEndpoint("public", "health", "/api/health", requiresAuthorization: false),
            MakeEndpoint("public", "get_orders", "/api/orders", requiresAuthorization: true));

        DocHasPath(doc, "/api/health").Should().BeFalse(
            "anonymous endpoint must be excluded when RequiresAuthorizationOnly=true");
        DocHasPath(doc, "/api/orders").Should().BeTrue(
            "authenticated endpoint must be included");
    }

    [Fact]
    public void RequiresAuthorizationOnly_false_admits_both_anon_and_authenticated()
    {
        // Sanity check on the default — RequiresAuthorizationOnly=false is the existing behavior.
        var doc = RunHandler(
            new OpenApiOptions { RequiresAuthorizationOnly = false },
            MakeEndpoint("public", "health", "/api/health", requiresAuthorization: false),
            MakeEndpoint("public", "get_orders", "/api/orders", requiresAuthorization: true));

        DocHasPath(doc, "/api/health").Should().BeTrue();
        DocHasPath(doc, "/api/orders").Should().BeTrue();
    }

    // ------------------------------------------------------------------------
    // IncludeSchemas
    // ------------------------------------------------------------------------

    [Fact]
    public void IncludeSchemas_keeps_only_listed_schemas()
    {
        var doc = RunHandler(
            new OpenApiOptions { IncludeSchemas = ["partner"] },
            MakeEndpoint("partner", "fn_a", "/api/partner-a"),
            MakeEndpoint("internal", "fn_b", "/api/internal-b"),
            MakeEndpoint("public", "fn_c", "/api/public-c"));

        DocHasPath(doc, "/api/partner-a").Should().BeTrue("schema 'partner' is allowed");
        DocHasPath(doc, "/api/internal-b").Should().BeFalse("schema 'internal' is not in IncludeSchemas");
        DocHasPath(doc, "/api/public-c").Should().BeFalse("schema 'public' is not in IncludeSchemas");
    }

    [Fact]
    public void IncludeSchemas_null_or_empty_admits_all()
    {
        // Default behavior — no filtering. Important: empty array and null both mean "no filter".
        var doc = RunHandler(
            new OpenApiOptions { IncludeSchemas = [] },
            MakeEndpoint("partner", "fn_a", "/api/a"),
            MakeEndpoint("internal", "fn_b", "/api/b"));

        DocHasPath(doc, "/api/a").Should().BeTrue();
        DocHasPath(doc, "/api/b").Should().BeTrue();
    }

    // ------------------------------------------------------------------------
    // ExcludeSchemas
    // ------------------------------------------------------------------------

    [Fact]
    public void ExcludeSchemas_removes_listed_schemas()
    {
        var doc = RunHandler(
            new OpenApiOptions { ExcludeSchemas = ["internal"] },
            MakeEndpoint("public", "fn_a", "/api/public-a"),
            MakeEndpoint("internal", "fn_b", "/api/internal-b"));

        DocHasPath(doc, "/api/public-a").Should().BeTrue();
        DocHasPath(doc, "/api/internal-b").Should().BeFalse(
            "schema 'internal' is in ExcludeSchemas — endpoint must be skipped");
    }

    [Fact]
    public void Include_and_Exclude_both_apply()
    {
        // Include narrows to {partner, billing}; Exclude then drops 'billing'. End result: only partner.
        var doc = RunHandler(
            new OpenApiOptions { IncludeSchemas = ["partner", "billing"], ExcludeSchemas = ["billing"] },
            MakeEndpoint("partner", "fn_a", "/api/partner-a"),
            MakeEndpoint("billing", "fn_b", "/api/billing-b"),
            MakeEndpoint("internal", "fn_c", "/api/internal-c"));

        DocHasPath(doc, "/api/partner-a").Should().BeTrue("partner allowed, not excluded");
        DocHasPath(doc, "/api/billing-b").Should().BeFalse("billing allowed but excluded");
        DocHasPath(doc, "/api/internal-c").Should().BeFalse("internal not in allow-list");
    }

    // ------------------------------------------------------------------------
    // NameSimilarTo (PostgreSQL SIMILAR TO → .NET regex)
    // ------------------------------------------------------------------------

    [Fact]
    public void NameSimilarTo_percent_wildcard_matches_prefix()
    {
        // The canonical "expose only partner-prefixed routines" pattern.
        var doc = RunHandler(
            new OpenApiOptions { NameSimilarTo = "partner_%" },
            MakeEndpoint("public", "partner_get_orders", "/api/partner-get-orders"),
            MakeEndpoint("public", "partner_create_order", "/api/partner-create-order"),
            MakeEndpoint("public", "internal_audit", "/api/internal-audit"));

        DocHasPath(doc, "/api/partner-get-orders").Should().BeTrue();
        DocHasPath(doc, "/api/partner-create-order").Should().BeTrue();
        DocHasPath(doc, "/api/internal-audit").Should().BeFalse(
            "name 'internal_audit' does not start with 'partner_' — must be filtered out");
    }

    [Fact]
    public void NameSimilarTo_underscore_matches_single_char()
    {
        // SIMILAR TO underscore is single-char, not any-sequence.
        var doc = RunHandler(
            new OpenApiOptions { NameSimilarTo = "f__" }, // matches 3-char names starting with 'f'
            MakeEndpoint("public", "foo", "/api/foo"),
            MakeEndpoint("public", "fooo", "/api/fooo"),
            MakeEndpoint("public", "bar", "/api/bar"));

        DocHasPath(doc, "/api/foo").Should().BeTrue("'foo' is f + 2 chars");
        DocHasPath(doc, "/api/fooo").Should().BeFalse("'fooo' is f + 3 chars, not f + 2");
        DocHasPath(doc, "/api/bar").Should().BeFalse();
    }

    [Fact]
    public void NameSimilarTo_alternation_supported()
    {
        // SIMILAR TO supports `|` like regex — passes through unchanged.
        var doc = RunHandler(
            new OpenApiOptions { NameSimilarTo = "(get|set)_%" },
            MakeEndpoint("public", "get_orders", "/api/get-orders"),
            MakeEndpoint("public", "set_status", "/api/set-status"),
            MakeEndpoint("public", "delete_order", "/api/delete-order"));

        DocHasPath(doc, "/api/get-orders").Should().BeTrue();
        DocHasPath(doc, "/api/set-status").Should().BeTrue();
        DocHasPath(doc, "/api/delete-order").Should().BeFalse();
    }

    [Fact]
    public void NameSimilarTo_is_anchored()
    {
        // SIMILAR TO is anchored: the pattern must match the whole name. So 'get_%' must NOT match
        // 'pre_get_x' just because the substring 'get_x' would.
        var doc = RunHandler(
            new OpenApiOptions { NameSimilarTo = "get_%" },
            MakeEndpoint("public", "get_orders", "/api/get-orders"),
            MakeEndpoint("public", "pre_get_x", "/api/pre-get-x"));

        DocHasPath(doc, "/api/get-orders").Should().BeTrue();
        DocHasPath(doc, "/api/pre-get-x").Should().BeFalse(
            "SIMILAR TO is anchored — prefix-only match must not pass an anchored pattern");
    }

    // ------------------------------------------------------------------------
    // NameNotSimilarTo
    // ------------------------------------------------------------------------

    [Fact]
    public void NameNotSimilarTo_excludes_matches()
    {
        var doc = RunHandler(
            new OpenApiOptions { NameNotSimilarTo = "%_internal" },
            MakeEndpoint("public", "orders", "/api/orders"),
            MakeEndpoint("public", "report_internal", "/api/report-internal"));

        DocHasPath(doc, "/api/orders").Should().BeTrue();
        DocHasPath(doc, "/api/report-internal").Should().BeFalse(
            "name ends with '_internal' — must be filtered out by NameNotSimilarTo");
    }

    [Fact]
    public void NameSimilarTo_and_NameNotSimilarTo_compose()
    {
        // Pattern: include everything starting with 'partner_', except routines ending in '_admin'.
        var doc = RunHandler(
            new OpenApiOptions
            {
                NameSimilarTo = "partner_%",
                NameNotSimilarTo = "%_admin"
            },
            MakeEndpoint("public", "partner_get_orders", "/api/partner-get-orders"),
            MakeEndpoint("public", "partner_delete_admin", "/api/partner-delete-admin"),
            MakeEndpoint("public", "internal_x", "/api/internal-x"));

        DocHasPath(doc, "/api/partner-get-orders").Should().BeTrue("matches include, not exclude");
        DocHasPath(doc, "/api/partner-delete-admin").Should().BeFalse(
            "matches include AND matches exclude — exclude wins");
        DocHasPath(doc, "/api/internal-x").Should().BeFalse("doesn't match include at all");
    }

    // ------------------------------------------------------------------------
    // Composition: multiple filter types together (the realistic scenario)
    // ------------------------------------------------------------------------

    [Fact]
    public void All_filters_apply_together()
    {
        // The full partner-API scenario: only partner schema, only routines starting with 'pub_',
        // only authenticated endpoints, and the @openapi hide annotation always wins on top.
        var doc = RunHandler(
            new OpenApiOptions
            {
                IncludeSchemas = ["partner"],
                NameSimilarTo = "pub_%",
                RequiresAuthorizationOnly = true
            },
            MakeEndpoint("partner", "pub_get_orders", "/api/pub-get-orders", requiresAuthorization: true),
            MakeEndpoint("partner", "pub_get_invoices", "/api/pub-get-invoices", requiresAuthorization: false),
            MakeEndpoint("partner", "internal_sweep", "/api/internal-sweep", requiresAuthorization: true),
            MakeEndpoint("internal", "pub_get_audit", "/api/audit", requiresAuthorization: true),
            MakeEndpoint("partner", "pub_get_hidden", "/api/pub-get-hidden", requiresAuthorization: true, openApiHide: true));

        DocHasPath(doc, "/api/pub-get-orders").Should().BeTrue("passes all filters");
        DocHasPath(doc, "/api/pub-get-invoices").Should().BeFalse("fails RequiresAuthorizationOnly");
        DocHasPath(doc, "/api/internal-sweep").Should().BeFalse("fails NameSimilarTo");
        DocHasPath(doc, "/api/audit").Should().BeFalse("fails IncludeSchemas");
        DocHasPath(doc, "/api/pub-get-hidden").Should().BeFalse("OpenApiHide always wins");
    }

    // ------------------------------------------------------------------------
    // Tag override (consumed from RoutineEndpoint.OpenApiTags)
    // ------------------------------------------------------------------------

    [Fact]
    public void OpenApiTags_replaces_default_schema_tag()
    {
        var doc = RunHandler(
            new OpenApiOptions(),
            MakeEndpoint("internal", "fn_a", "/api/fn-a", openApiTags: ["partner"]));

        var tags = doc["paths"]?["/api/fn-a"]?["get"]?["tags"]?.AsArray();
        tags.Should().NotBeNull();
        tags!.Count.Should().Be(1);
        tags[0]!.GetValue<string>().Should().Be("partner",
            "OpenApiTags overrides the default schema-name tag — letting authors group by audience " +
            "(partner/internal/admin) rather than implementation layout (schema name)");
    }

    [Fact]
    public void OpenApiTags_null_falls_back_to_schema_tag()
    {
        var doc = RunHandler(
            new OpenApiOptions(),
            MakeEndpoint("partner", "fn_b", "/api/fn-b", openApiTags: null));

        var tags = doc["paths"]?["/api/fn-b"]?["get"]?["tags"]?.AsArray();
        tags.Should().NotBeNull();
        tags!.Count.Should().Be(1);
        tags[0]!.GetValue<string>().Should().Be("partner", "default tag is the schema name");
    }
}
