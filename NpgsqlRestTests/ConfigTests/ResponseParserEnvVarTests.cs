using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using NpgsqlRest.Auth;
using NpgsqlRestClient;

namespace NpgsqlRestTests.ConfigTests;

/// <summary>
/// Unit tests for the env-var injection path in <see cref="DefaultResponseParser"/>.
/// Env vars named in <c>StaticFiles:ParseContentOptions:AvailableEnvVars</c> are resolved once at
/// parser construction, JSON-escaped (like claims), and substituted into the same <c>{NAME}</c> tag
/// machinery the claim path uses. See STATIC_CONTENT_ENV_VAR_INJECTION.md.
/// </summary>
public class ResponseParserEnvVarTests
{
    private static readonly NpgsqlRestAuthenticationOptions Options = new();

    private static DefaultResponseParser Parser(
        Dictionary<string, string?>? claims = null,
        Dictionary<string, string?>? envVars = null) =>
        new(Options, antiforgeryFieldNameTag: null, antiforgeryTokenTag: null, claims, envVars);

    private static HttpContext Anonymous()
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity());
        return ctx;
    }

    private static HttpContext WithClaims(params (string type, string value)[] claims)
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
            claims.Select(c => new Claim(c.type, c.value)), authenticationType: "test"));
        return ctx;
    }

    private static string Parse(DefaultResponseParser parser, string input, HttpContext ctx) =>
        parser.Parse(input.AsSpan(), ctx, tokenSet: null).ToString();

    [Fact]
    public void EmptyAvailableEnvVars_PassesContentThrough()
    {
        var parser = Parser(envVars: null);
        const string input = "buildLabel: {BUILD_LABEL_EMPTY_CASE};";
        // No env vars configured -> the {BUILD_LABEL_EMPTY_CASE} token is unknown and left verbatim.
        Parse(parser, input, Anonymous()).Should().Be("buildLabel: {BUILD_LABEL_EMPTY_CASE};");
    }

    [Fact]
    public void EnvVarPresent_SubstitutesJsonEscapedValue()
    {
        const string name = "NPGSQLREST_TEST_BUILD_LABEL";
        Environment.SetEnvironmentVariable(name, "demo");
        try
        {
            var parser = Parser(envVars: new() { [name] = null });
            // bare token -> the substituted value is a complete, quoted JSON literal
            Parse(parser, $"buildLabel: {{{name}}};", Anonymous()).Should().Be("buildLabel: \"demo\";");
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public void EnvVarMissing_ArrayForm_BecomesEmptyJsonString()
    {
        const string name = "NPGSQLREST_TEST_MISSING_NO_DEFAULT";
        Environment.SetEnvironmentVariable(name, null); // ensure absent
        var parser = Parser(envVars: new() { [name] = null });
        // missing + no default -> empty string -> JSON "" (never the literal text "null")
        Parse(parser, $"x: {{{name}}};", Anonymous()).Should().Be("x: \"\";");
    }

    [Fact]
    public void EnvVarMissing_ObjectFormDefault_UsesDefault()
    {
        const string name = "NPGSQLREST_TEST_MISSING_WITH_DEFAULT";
        Environment.SetEnvironmentVariable(name, null); // ensure absent
        var parser = Parser(envVars: new() { [name] = "false" });
        Parse(parser, $"demoMode: {{{name}}} === \"true\";", Anonymous())
            .Should().Be("demoMode: \"false\" === \"true\";");
    }

    [Fact]
    public void EnvVarValueWithQuotes_IsJsonEscaped()
    {
        const string name = "NPGSQLREST_TEST_ESCAPE";
        Environment.SetEnvironmentVariable(name, "a\"b\\c");
        try
        {
            var parser = Parser(envVars: new() { [name] = null });
            // " -> \" and \ -> \\ , so it stays a single well-formed JS string literal
            Parse(parser, $"v: {{{name}}};", Anonymous()).Should().Be("v: \"a\\\"b\\\\c\";");
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public void MultipleEnvVars_AllSubstitutedInOnePass()
    {
        const string a = "NPGSQLREST_TEST_MULTI_A";
        const string b = "NPGSQLREST_TEST_MULTI_B";
        Environment.SetEnvironmentVariable(a, "one");
        Environment.SetEnvironmentVariable(b, "two");
        try
        {
            var parser = Parser(envVars: new() { [a] = null, [b] = null });
            Parse(parser, $"{{{a}}}-{{{b}}}", Anonymous()).Should().Be("\"one\"-\"two\"");
        }
        finally
        {
            Environment.SetEnvironmentVariable(a, null);
            Environment.SetEnvironmentVariable(b, null);
        }
    }

    [Fact]
    public void ClaimAndEnvVarNameCollision_ClaimWins()
    {
        const string name = "shared_token";
        Environment.SetEnvironmentVariable(name, "from_env");
        try
        {
            var parser = Parser(envVars: new() { [name] = null });
            // a claim of the same name is present -> per-request claim value wins over the env var
            var result = Parse(parser, $"v: {{{name}}};", WithClaims((name, "from_claim")));
            result.Should().Be("v: \"from_claim\";");
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public void AnonymousRequest_EnvVarsStillSubstituted()
    {
        const string name = "NPGSQLREST_TEST_ANON";
        Environment.SetEnvironmentVariable(name, "anon_ok");
        try
        {
            var parser = Parser(envVars: new() { [name] = null });
            Parse(parser, $"v: {{{name}}};", Anonymous()).Should().Be("v: \"anon_ok\";");
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public void ClaimObjectFormDefault_UsedWhenClaimAbsent()
    {
        // listed-but-absent claim with an explicit default -> default, not null
        var withDefault = Parser(claims: new() { ["plan"] = "free" });
        Parse(withDefault, "plan: {plan};", Anonymous()).Should().Be("plan: free;");

        // array form (no default) -> historical null behaviour
        var arrayForm = Parser(claims: new() { ["plan"] = null });
        Parse(arrayForm, "plan: {plan};", Anonymous()).Should().Be("plan: null;");
    }

    // ---- strict forms {!NAME} / {!NAME:fallback} ----

    [Fact]
    public void StrictForms_ResolvedVar_ValueWinsOverInlineFallback()
    {
        const string name = "NPGSQLREST_TEST_STRICT_SET";
        Environment.SetEnvironmentVariable(name, "demo");
        try
        {
            var parser = Parser(envVars: new() { [name] = null });
            Parse(parser, $"a: {{!{name}}}; b: {{!{name}:fb}};", Anonymous())
                .Should().Be("a: \"demo\"; b: \"demo\";");
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public void StrictForms_UnresolvedVar_InlineFallbackUsed()
    {
        const string name = "NPGSQLREST_TEST_STRICT_UNSET";
        Environment.SetEnvironmentVariable(name, null); // ensure absent, no configured default
        var parser = Parser(envVars: new() { [name] = null });
        // {!NAME} behaves like {NAME} (empty JSON string); the inline fallback is raw literal text
        // (the template author quotes it as needed), used only when the var is allowlisted-but-unresolved
        Parse(parser, $"a: {{!{name}}}; b: {{!{name}:\"fb\"}};", Anonymous())
            .Should().Be("a: \"\"; b: \"fb\";");
    }

    [Fact]
    public void StrictForms_ConfiguredDefault_WinsOverInlineFallback()
    {
        const string name = "NPGSQLREST_TEST_STRICT_DEFAULT";
        Environment.SetEnvironmentVariable(name, null); // ensure absent
        var parser = Parser(envVars: new() { [name] = "false" });
        Parse(parser, $"v: {{!{name}:true}};", Anonymous()).Should().Be("v: \"false\";");
    }

    [Fact]
    public void StrictForms_UnlistedName_LeftVerbatim()
    {
        // the allowlist is a security boundary: the strict forms never widen it
        var parser = Parser(envVars: new() { ["NPGSQLREST_TEST_STRICT_OTHER"] = "x" });
        Parse(parser, "v: {!NOT_LISTED_ANYWHERE:fb};", Anonymous())
            .Should().Be("v: {!NOT_LISTED_ANYWHERE:fb};");
    }

    [Fact]
    public void StrictForms_PresentClaim_BeatsInlineFallback()
    {
        var parser = Parser();
        Parse(parser, "plan: {!plan:none};", WithClaims(("plan", "pro")))
            .Should().Be("plan: \"pro\";");
    }

    [Fact]
    public void StrictForms_AbsentClaim_NoDefault_InlineFallbackUsed()
    {
        // listed claim without a default -> plain token gives the historical null, the strict
        // form gives the inline fallback
        var parser = Parser(claims: new() { ["plan"] = null });
        Parse(parser, "a: {plan}; b: {!plan:\"free\"};", Anonymous())
            .Should().Be("a: null; b: \"free\";");
    }

    [Fact]
    public void StrictForms_CssContent_NeverConsumed()
    {
        var parser = Parser(envVars: new() { ["NPGSQLREST_TEST_STRICT_CSS"] = "x" });
        const string css = "td{border:1px solid #d4d4d4;padding:4px 8px} h1{color:red !important}";
        Parse(parser, css, Anonymous()).Should().Be(css);
    }

    [Fact]
    public void EnvVarResolvedAtConstruction_NotReReadPerRequest()
    {
        const string name = "NPGSQLREST_TEST_CACHED";
        Environment.SetEnvironmentVariable(name, "first");
        try
        {
            var parser = Parser(envVars: new() { [name] = null });

            var firstCall = Parse(parser, $"v: {{{name}}};", Anonymous());
            firstCall.Should().Be("v: \"first\";");

            // mutate the process env AFTER construction
            Environment.SetEnvironmentVariable(name, "second");

            // value was captured at construction -> still "first", proving no per-request re-read
            var secondCall = Parse(parser, $"v: {{{name}}};", Anonymous());
            secondCall.Should().Be("v: \"first\";");
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }
}
