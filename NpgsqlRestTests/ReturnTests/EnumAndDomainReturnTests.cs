namespace NpgsqlRestTests;

public static partial class Database
{
    public static void EnumAndDomainReturnTests()
    {
        script.Append(
        """
        create type enum_domain_mood as enum ('happy', 'sad');
        create domain enum_domain_text as text;

        create function get_enum_domain_values()
        returns table(mood enum_domain_mood, label enum_domain_text, empty_label enum_domain_text)
        language sql
        as
        $$
        select 'happy'::enum_domain_mood, '"x","isAdmin":true,"y":"z'::enum_domain_text, ''::enum_domain_text
        $$;
        """);
    }
}

/// <summary>
/// Regression: enum and domain columns (types without a specific mapping) must be written as escaped JSON strings.
/// They used to be written raw: enums produced invalid JSON ({"mood":happy}), a domain over text let stored data
/// inject JSON keys, and an empty value failed with HTTP 500.
/// </summary>
[Collection("TestFixture")]
public class EnumAndDomainReturnTests(TestFixture test)
{
    [Fact]
    public async Task Enum_and_domain_columns_are_json_strings()
    {
        using var client = test.Application.CreateClient();
        client.Timeout = TimeSpan.FromHours(1);

        using var response = await client.GetAsync("/api/get-enum-domain-values/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("""[{"mood":"happy","label":"\"x\",\"isAdmin\":true,\"y\":\"z","emptyLabel":""}]""");
    }
}
