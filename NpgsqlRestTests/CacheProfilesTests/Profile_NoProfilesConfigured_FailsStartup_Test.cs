using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace NpgsqlRestTests;

public static partial class Database
{
    /// <summary>
    /// Setup for Profile_NoProfilesConfigured_FailsStartup_Test.
    /// Endpoint references a profile by name, but no Profiles dictionary is configured at all.
    /// </summary>
    public static void Profile_NoProfilesConfigured_FailsStartup_Test()
    {
        script.Append(@"
        create function cpx_no_profiles_configured()
        returns text language sql as $$ select 'x'::text $$;
        comment on function cpx_no_profiles_configured() is '
        HTTP GET
        cache_profile some_profile
        ';
        ");
    }
}

public class Profile_NoProfilesConfigured_FailsStartup_Test
{
    /// <summary>
    /// If an endpoint annotation references a cache profile but the application has no Profiles
    /// dictionary configured at all, startup must fail with a clear error mentioning the offending
    /// profile name and endpoint. We don't silently degrade to "no caching" because that would mask
    /// the configuration mistake.
    /// </summary>
    [Fact]
    public void Endpoint_referencing_profile_with_no_profiles_dictionary_throws_on_startup()
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
                    NameSimilarTo = "cpx_no_profiles_configured",
                    CommentsMode = CommentsMode.ParseAll,
                    RequiresAuthorization = false,
                    CacheOptions = new()
                    {
                        DefaultRoutineCache = new RoutineCache()
                        // Profiles intentionally null
                    }
                }));

            ex.Message.Should().Contain("'some_profile'");
            ex.Message.Should().Contain("/api/cpx-no-profiles-configured");
            ex.Message.Should().Contain("no profiles are configured");
        }
        finally
        {
            app.DisposeAsync().GetAwaiter().GetResult();
        }
    }
}
