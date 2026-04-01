namespace NpgsqlRestTests.SqlFileSourceTests;

public static partial class SqlFiles
{
    public static void ParamDefaultValueTests()
    {
        // Default null — parameter is optional, gets NULL when not provided
        File.WriteAllText(Path.Combine(Dir, "default_null_param.sql"), """
            -- @param $1 my_name
            -- @param my_name default null
            select coalesce($1, 'was_null') as result;
            """);

        // Default text — parameter gets text value when not provided
        File.WriteAllText(Path.Combine(Dir, "default_text_param.sql"), """
            -- @param $1 greeting
            -- @param greeting default 'hello world'
            select $1 as result;
            """);

        // Default boolean — parameter gets boolean value when not provided
        File.WriteAllText(Path.Combine(Dir, "default_bool_param.sql"), """
            -- @param $1 is_active
            -- @param is_active default true
            select $1::boolean as result;
            """);

        // Default number — parameter gets numeric value when not provided
        File.WriteAllText(Path.Combine(Dir, "default_number_param.sql"), """
            -- @param $1 amount
            -- @param amount default 42
            select $1::integer as result;
            """);

        // Default with no value — treated as default null
        File.WriteAllText(Path.Combine(Dir, "default_bare_param.sql"), """
            -- @param $1 val
            -- @param val default
            select coalesce($1, 'was_null') as result;
            """);

        // Two params, one with default — mixed required and optional
        File.WriteAllText(Path.Combine(Dir, "default_mixed_params.sql"), """
            -- @param $1 name
            -- @param $2 greeting
            -- @param greeting default 'hi'
            select $2 || ' ' || $1 as result;
            """);

        // Default with single-word quoted value — literal text 'null', not DB NULL
        File.WriteAllText(Path.Combine(Dir, "default_quoted_single.sql"), """
            -- @param $1 tag
            -- @param tag default 'null'
            select $1 as result;
            """);

        // Combined rename + default on single line
        File.WriteAllText(Path.Combine(Dir, "default_combined_param.sql"), """
            -- @param $1 _user_id default null
            -- @param $2 _username default null
            -- @param $3 _email default 'unknown'
            select coalesce($1, 'no_id') as user_id, coalesce($2, 'no_name') as username, $3 as email;
            """);

        // "is" style rename + default: @param $1 is name default value
        File.WriteAllText(Path.Combine(Dir, "default_is_style.sql"), """
            -- @param $1 is greeting default 'hey'
            select $1 as result;
            """);

        // Default without rename — use positional param name directly
        File.WriteAllText(Path.Combine(Dir, "default_no_rename.sql"), """
            -- @param $1 default 'fallback'
            select $1 as result;
            """);

        // = alias for default — null
        File.WriteAllText(Path.Combine(Dir, "equals_null_param.sql"), """
            -- @param $1 my_val
            -- @param my_val = null
            select coalesce($1, 'was_null') as result;
            """);

        // = alias for default — text value
        File.WriteAllText(Path.Combine(Dir, "equals_text_param.sql"), """
            -- @param $1 greeting
            -- @param greeting = 'hello world'
            select $1 as result;
            """);

        // = alias for default — combined rename + type + default on single line
        File.WriteAllText(Path.Combine(Dir, "equals_combined_param.sql"), """
            -- @param $1 _user_id text = null
            -- @param $2 _username text = 'anonymous'
            select coalesce($1, 'no_id') as user_id, $2 as username;
            """);

        // = alias for default — "is" style rename + default
        File.WriteAllText(Path.Combine(Dir, "equals_is_style.sql"), """
            -- @param $1 is greeting = 'hey'
            select $1 as result;
            """);

        // = alias for default — bare (no value = null)
        File.WriteAllText(Path.Combine(Dir, "equals_bare_param.sql"), """
            -- @param $1 val
            -- @param val =
            select coalesce($1, 'was_null') as result;
            """);
    }
}

[Collection("SqlFileSourceFixture")]
public class ParamDefaultValueTests(SqlFileSourceTestFixture test)
{
    // --- Default null ---

    [Fact]
    public async Task DefaultNull_NotProvided_ReturnsCoalescedValue()
    {
        using var response = await test.Client.GetAsync("/api/default-null-param");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("was_null");
    }

    [Fact]
    public async Task DefaultNull_Provided_ReturnsProvidedValue()
    {
        using var response = await test.Client.GetAsync("/api/default-null-param?my_name=alice");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("alice");
    }

    // --- Default text ---

    [Fact]
    public async Task DefaultText_NotProvided_ReturnsDefaultText()
    {
        using var response = await test.Client.GetAsync("/api/default-text-param");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("hello world");
    }

    [Fact]
    public async Task DefaultText_Provided_ReturnsProvidedValue()
    {
        using var response = await test.Client.GetAsync("/api/default-text-param?greeting=goodbye");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("goodbye");
    }

    // --- Default boolean ---

    [Fact]
    public async Task DefaultBool_NotProvided_ReturnsDefaultTrue()
    {
        using var response = await test.Client.GetAsync("/api/default-bool-param");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("true");
    }

    [Fact]
    public async Task DefaultBool_Provided_ReturnsProvidedValue()
    {
        using var response = await test.Client.GetAsync("/api/default-bool-param?is_active=false");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("false");
    }

    // --- Default number ---

    [Fact]
    public async Task DefaultNumber_NotProvided_ReturnsDefault42()
    {
        using var response = await test.Client.GetAsync("/api/default-number-param");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("42");
    }

    [Fact]
    public async Task DefaultNumber_Provided_ReturnsProvidedValue()
    {
        using var response = await test.Client.GetAsync("/api/default-number-param?amount=99");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("99");
    }

    // --- Default bare (no value = null) ---

    [Fact]
    public async Task DefaultBare_NotProvided_ReturnsCoalescedNull()
    {
        using var response = await test.Client.GetAsync("/api/default-bare-param");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("was_null");
    }

    // --- Mixed params: one required, one with default ---

    [Fact]
    public async Task MixedParams_OnlyRequired_UsesDefault()
    {
        using var response = await test.Client.GetAsync("/api/default-mixed-params?name=alice");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("hi alice");
    }

    [Fact]
    public async Task MixedParams_BothProvided_UsesProvided()
    {
        using var response = await test.Client.GetAsync("/api/default-mixed-params?name=alice&greeting=hey");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("hey alice");
    }

    [Fact]
    public async Task MixedParams_MissingRequired_Returns404()
    {
        using var response = await test.Client.GetAsync("/api/default-mixed-params?greeting=hey");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // --- Quoted 'null' (literal text, not DB NULL) ---

    [Fact]
    public async Task QuotedNull_NotProvided_ReturnsLiteralNullText()
    {
        using var response = await test.Client.GetAsync("/api/default-quoted-single");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        // Should be the literal text "null", not a JSON null
        content.Should().Be("""["null"]""");
    }

    // --- Combined rename + default on single line ---

    [Fact]
    public async Task Combined_NoneProvided_AllUseDefaults()
    {
        using var response = await test.Client.GetAsync("/api/default-combined-param");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        doc.RootElement[0].GetProperty("userId").GetString().Should().Be("no_id");
        doc.RootElement[0].GetProperty("username").GetString().Should().Be("no_name");
        doc.RootElement[0].GetProperty("email").GetString().Should().Be("unknown");
    }

    [Fact]
    public async Task Combined_SomeProvided_MixesProvidedAndDefaults()
    {
        using var response = await test.Client.GetAsync("/api/default-combined-param?_email=test@example.com");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        doc.RootElement[0].GetProperty("userId").GetString().Should().Be("no_id");
        doc.RootElement[0].GetProperty("username").GetString().Should().Be("no_name");
        doc.RootElement[0].GetProperty("email").GetString().Should().Be("test@example.com");
    }

    // --- "is" style rename + default ---

    [Fact]
    public async Task IsStyle_NotProvided_ReturnsDefault()
    {
        using var response = await test.Client.GetAsync("/api/default-is-style");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("hey");
    }

    [Fact]
    public async Task IsStyle_Provided_ReturnsProvided()
    {
        using var response = await test.Client.GetAsync("/api/default-is-style?greeting=yo");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("yo");
    }

    // --- Default without rename (use $1 directly) ---

    [Fact]
    public async Task DefaultNoRename_NotProvided_ReturnsDefault()
    {
        using var response = await test.Client.GetAsync("/api/default-no-rename");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("fallback");
    }

    // --- = alias for default null ---

    [Fact]
    public async Task EqualsNull_NotProvided_ReturnsCoalescedValue()
    {
        using var response = await test.Client.GetAsync("/api/equals-null-param");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("was_null");
    }

    [Fact]
    public async Task EqualsNull_Provided_ReturnsProvidedValue()
    {
        using var response = await test.Client.GetAsync("/api/equals-null-param?my_val=alice");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("alice");
    }

    // --- = alias for default text ---

    [Fact]
    public async Task EqualsText_NotProvided_ReturnsDefaultText()
    {
        using var response = await test.Client.GetAsync("/api/equals-text-param");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("hello world");
    }

    [Fact]
    public async Task EqualsText_Provided_ReturnsProvidedValue()
    {
        using var response = await test.Client.GetAsync("/api/equals-text-param?greeting=goodbye");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("goodbye");
    }

    // --- = alias combined rename + type + default ---

    [Fact]
    public async Task EqualsCombined_NoneProvided_AllUseDefaults()
    {
        using var response = await test.Client.GetAsync("/api/equals-combined-param");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        doc.RootElement[0].GetProperty("userId").GetString().Should().Be("no_id");
        doc.RootElement[0].GetProperty("username").GetString().Should().Be("anonymous");
    }

    [Fact]
    public async Task EqualsCombined_SomeProvided_MixesProvidedAndDefaults()
    {
        using var response = await test.Client.GetAsync("/api/equals-combined-param?_username=bob");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        doc.RootElement[0].GetProperty("userId").GetString().Should().Be("no_id");
        doc.RootElement[0].GetProperty("username").GetString().Should().Be("bob");
    }

    // --- = alias "is" style ---

    [Fact]
    public async Task EqualsIsStyle_NotProvided_ReturnsDefault()
    {
        using var response = await test.Client.GetAsync("/api/equals-is-style");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("hey");
    }

    [Fact]
    public async Task EqualsIsStyle_Provided_ReturnsProvided()
    {
        using var response = await test.Client.GetAsync("/api/equals-is-style?greeting=yo");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("yo");
    }

    // --- = alias bare (no value = null) ---

    [Fact]
    public async Task EqualsBare_NotProvided_ReturnsCoalescedNull()
    {
        using var response = await test.Client.GetAsync("/api/equals-bare-param");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("was_null");
    }
}
