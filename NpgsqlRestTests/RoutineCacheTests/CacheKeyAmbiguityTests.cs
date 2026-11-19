namespace NpgsqlRestTests;

public static partial class Database
{
    public static void CacheKeyAmbiguityTests()
    {
        script.Append(@"
create function cache_amb_two(_a text default null, _b text default null)
returns text
language sql
as $$ select coalesce(_a, '-') || '|' || coalesce(_b, '-') || '|' || random()::text $$;
comment on function cache_amb_two(text, text) is 'HTTP GET
cached _a, _b';

create function cache_amb_user(_user_id text default null, _query text default null)
returns text
language sql
as $$ select coalesce(_user_id, 'anon') || '|' || coalesce(_query, '-') || '|' || random()::text $$;
comment on function cache_amb_user(text, text) is 'HTTP GET
user_params
cached _user_id, _query';
");
    }
}

/// <summary>
/// Security regression: response-cache keys must be unambiguous. They used to be the routine plus the values of the
/// supplied cached parameters, without names and without lengths, so omitted parameters were indistinguishable and a
/// value containing the separator (%1F) could forge another parameter list - including across users.
/// </summary>
[Collection("TestFixture")]
public class CacheKeyAmbiguityTests(TestFixture test)
{
    [Fact]
    public async Task Different_omitted_parameters_do_not_share_a_cache_entry()
    {
        var first = await test.Client.GetStringAsync("/api/cache-amb-two/?a=omit1");
        first.Should().StartWith("omit1|-|");

        var second = await test.Client.GetStringAsync("/api/cache-amb-two/?b=omit1");
        second.Should().StartWith("-|omit1|");
    }

    [Fact]
    public async Task A_value_containing_the_separator_cannot_forge_another_parameter_list()
    {
        var first = await test.Client.GetStringAsync("/api/cache-amb-two/?a=sep1&b=sep2");
        first.Should().StartWith("sep1|sep2|");

        var forged = await test.Client.GetStringAsync("/api/cache-amb-two/?a=sep1%1Fsep2");
        forged.Should().StartWith("sep1\u001fsep2|-|");
    }

    [Fact]
    public async Task Anonymous_request_does_not_get_a_users_cached_response()
    {
        using var user = test.Application.CreateClient();
        using var login = await user.GetAsync("/login"); // name_identifier claim "user123" -> _user_id
        var userResponse = await user.GetStringAsync("/api/cache-amb-user/");
        userResponse.Should().StartWith("user123|-|");

        // anonymous caller puts the same value in a different parameter
        var anonymousResponse = await test.Client.GetStringAsync("/api/cache-amb-user/?query=user123");
        anonymousResponse.Should().StartWith("anon|user123|");
    }
}
