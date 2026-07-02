namespace NpgsqlRestTests.SqlFileSourceTests;

public static partial class SqlFiles
{
    public static void NamedParamEndpointTests()
    {
        // Named placeholders: API names come from the placeholders (through the camelCase NameConverter),
        // no @param annotations needed; a repeated name maps to the SAME parameter.
        File.WriteAllText(Path.Combine(Dir, "named_echo.sql"), """
            /*
            HTTP GET
            */
            select :first_name || ' ' || :last_name || ' (' || :first_name || ')' as greeting;
            """);

        // Optional named parameter via the existing name-matching default annotation.
        File.WriteAllText(Path.Combine(Dir, "named_default.sql"), """
            -- @param label default null
            select coalesce(:label, 'none') as v;
            """);

        // Retype WITHOUT rename via the new `type is` form (both the Describe hint and endpoint retype).
        File.WriteAllText(Path.Combine(Dir, "named_type_is.sql"), """
            -- @param val type is int
            select :val as v;
            """);

        // Claim-mapped user parameter — the placeholder name itself (_user_id) matches
        // ParameterNameClaimsMapping, so the claim hookup needs no annotation at all.
        File.WriteAllText(Path.Combine(Dir, "named_user_param.sql"), """
            -- @authorize
            -- @user_parameters
            select :_user_id as user_id;
            """);

        // Mixing positional and named placeholders is rejected (ErrorMode=Skip → no endpoint).
        File.WriteAllText(Path.Combine(Dir, "named_mixed_invalid.sql"), """
            select $1 as a, :b as b;
            """);

        // Multi-command file: :id is shared ACROSS statements (one API parameter), :offset appears
        // only in the second statement — still no annotations needed.
        File.WriteAllText(Path.Combine(Dir, "named_multi.sql"), """
            -- HTTP POST
            -- @result first
            select :id::int as v;
            -- @result second
            select :id::int + :offset::int as v;
            """);
    }
}

[Collection("SqlFileSourceFixture")]
public class NamedParamEndpointTests(SqlFileSourceTestFixture test)
{
    [Fact]
    public async Task NamedParams_AutoNamed_CamelCased_And_RepeatedNameShared()
    {
        using var response = await test.Client.GetAsync("/api/named-echo?firstName=Ada&lastName=Lovelace");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[\"Ada Lovelace (Ada)\"]");
    }

    [Fact]
    public async Task NamedParams_MissingRequiredParam_NotFound()
    {
        // lastName missing → endpoint parameter set doesn't match → 404
        using var response = await test.Client.GetAsync("/api/named-echo?firstName=Ada");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task NamedParams_DefaultAnnotation_MatchesByPlaceholderName()
    {
        using var response = await test.Client.GetAsync("/api/named-default");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[\"none\"]");

        using var response2 = await test.Client.GetAsync("/api/named-default?label=tagged");
        var content2 = await response2.Content.ReadAsStringAsync();
        response2.StatusCode.Should().Be(HttpStatusCode.OK);
        content2.Should().Be("[\"tagged\"]");
    }

    [Fact]
    public async Task NamedParams_TypeIsForm_RetypesWithoutRename()
    {
        using var response = await test.Client.GetAsync("/api/named-type-is?val=7");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // int type → rendered unquoted in JSON
        content.Should().Be("[7]");
    }

    [Fact]
    public async Task NamedParams_ClaimMappedByPlaceholderName_NoAnnotationNeeded()
    {
        using var client = test.CreateClient();
        await client.GetAsync("/login");

        using var response = await client.GetAsync("/api/named-user-param");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // name_identifier claim = "user123"
        content.Should().Be("[\"user123\"]");
    }

    [Fact]
    public async Task MixedPositionalAndNamed_FileSkipped_NoEndpoint()
    {
        using var response = await test.Client.GetAsync("/api/named-mixed-invalid?a=1&b=2");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task NamedParams_SharedAcrossStatements_OneApiParameter()
    {
        using var body = new StringContent("{\"id\": 10, \"offset\": 5}", Encoding.UTF8, "application/json");
        using var response = await test.Client.PostAsync("/api/named-multi", body);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        // one `id` value feeds both statements; `offset` only the second
        content.Should().Be("""{"first":[10],"second":[15]}""");
    }
}
