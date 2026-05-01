using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace NpgsqlRestTests;

public static partial class Database
{
    /// <summary>
    /// Setup for Profile_UnknownName_FailsStartup_Test.
    /// Defines two functions that reference NON-existent profiles by name. UseNpgsqlRest must throw
    /// at startup with a single error listing every unresolved name + the offending endpoints.
    /// </summary>
    public static void Profile_UnknownName_FailsStartup_Test()
    {
        script.Append(@"
        create function cpx_unknown_name_a()
        returns text language sql as $$ select 'a'::text $$;
        comment on function cpx_unknown_name_a() is '
        HTTP GET
        cache_profile this_profile_does_not_exist
        ';

        create function cpx_unknown_name_b()
        returns text language sql as $$ select 'b'::text $$;
        comment on function cpx_unknown_name_b() is '
        HTTP GET
        cache_profile another_missing_profile
        ';

        create function cpx_unknown_name_c()
        returns text language sql as $$ select 'c'::text $$;
        comment on function cpx_unknown_name_c() is '
        HTTP GET
        cache_profile this_profile_does_not_exist
        ';
        ");
    }
}

public class Profile_UnknownName_FailsStartup_Test
{
    /// <summary>
    /// Building an NpgsqlRest application that has profile-referencing endpoints but a typo'd
    /// (or missing) profile name must throw a single InvalidOperationException whose message lists:
    /// (a) every unresolved profile name, (b) the endpoints that referenced each name, and
    /// (c) the available profile names so the user can spot typos quickly.
    /// </summary>
    [Fact]
    public void UseNpgsqlRest_throws_listing_all_unresolved_profile_names_and_offending_endpoints()
    {
        var connectionString = Database.Create();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();

        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                app.UseNpgsqlRest(new(connectionString)
                {
                    IncludeSchemas = ["public"],
                    NameSimilarTo = "cpx_unknown_name_%",
                    CommentsMode = CommentsMode.ParseAll,
                    RequiresAuthorization = false,
                    CacheOptions = new()
                    {
                        DefaultRoutineCache = new RoutineCache(),
                        Profiles = new()
                        {
                            ["only_real_profile"] = new CacheProfile { Cache = new RoutineCache() }
                        }
                    }
                }));

            ex.Message.Should().Contain("Unknown cache profile name(s)");
            // Both unresolved names must be listed, regardless of order.
            ex.Message.Should().Contain("'this_profile_does_not_exist'");
            ex.Message.Should().Contain("'another_missing_profile'");
            // The profile referenced by two endpoints (a and c) should list both endpoint URLs.
            ex.Message.Should().Contain("/api/cpx-unknown-name-a");
            ex.Message.Should().Contain("/api/cpx-unknown-name-b");
            ex.Message.Should().Contain("/api/cpx-unknown-name-c");
            // The available profiles line should help the user spot typos.
            ex.Message.Should().Contain("Available profiles:");
            ex.Message.Should().Contain("only_real_profile");
        }
        finally
        {
            app.DisposeAsync().GetAwaiter().GetResult();
        }
    }
}
