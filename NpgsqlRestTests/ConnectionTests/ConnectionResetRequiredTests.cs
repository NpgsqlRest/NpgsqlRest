using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ConnectionResetRequiredTests()
    {
        script.Append("""
        create function crr_plain() returns text language sql stable as $$ select 'plain' $$;
        comment on function crr_plain() is 'HTTP GET';

        create function crr_user_context() returns text language sql stable as $$ select current_setting('request.user_id', true) $$;
        comment on function crr_user_context() is 'HTTP GET
        user_context';

        create function crr_headers_context() returns text language sql stable as $$ select current_setting('request.headers', true) $$;
        comment on function crr_headers_context() is 'HTTP GET
        request_headers context';
        """);
    }
}

/// <summary>
/// NpgsqlRestOptions.ConnectionResetRequired tells the host whether a request can leave session-level settings on its
/// connection - that is, whether pooled connections have to be reset between requests. UseNpgsqlRest sets it from
/// the endpoints it builds.
/// </summary>
public class ConnectionResetRequiredTests
{
    private static async Task<NpgsqlRestOptions> BuildAsync(string nameSimilarTo, Action<NpgsqlRestOptions>? configure = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        await using var app = builder.Build();

        var options = new NpgsqlRestOptions(Database.Create())
        {
            IncludeSchemas = ["public"],
            NameSimilarTo = nameSimilarTo,
            CommentsMode = CommentsMode.ParseAll,
            RequiresAuthorization = false,
        };
        configure?.Invoke(options);
        app.UseNpgsqlRest(options);
        return options;
    }

    [Fact]
    public async Task Not_required_when_no_endpoint_sets_a_context()
    {
        (await BuildAsync("crr[_]plain")).ConnectionResetRequired.Should().BeFalse();
    }

    [Fact]
    public async Task Required_when_an_endpoint_uses_user_context()
    {
        (await BuildAsync("crr[_](plain|user[_]context)")).ConnectionResetRequired.Should().BeTrue();
    }

    [Fact]
    public async Task Required_when_an_endpoint_uses_request_headers_context()
    {
        (await BuildAsync("crr[_](plain|headers[_]context)")).ConnectionResetRequired.Should().BeTrue();
    }

    [Fact]
    public async Task Required_when_before_routine_commands_are_configured()
    {
        (await BuildAsync("crr[_]plain", o => o.BeforeRoutineCommands = ["select set_config('app.tenant', 'x', false)"]))
            .ConnectionResetRequired.Should().BeTrue();
    }

    [Fact]
    public async Task Not_required_with_WrapInTransaction_because_settings_are_local_to_the_transaction()
    {
        (await BuildAsync("crr[_](plain|user[_]context|headers[_]context)", o =>
        {
            o.WrapInTransaction = true;
            o.BeforeRoutineCommands = ["select set_config('app.tenant', 'x', true)"];
        })).ConnectionResetRequired.Should().BeFalse();
    }

    [Fact]
    public async Task Not_required_when_user_context_has_no_context_keys_to_set()
    {
        (await BuildAsync("crr[_](plain|user[_]context)", o =>
        {
            o.AuthenticationOptions.IpAddressContextKey = null;
            o.AuthenticationOptions.ClaimsJsonContextKey = null;
            o.AuthenticationOptions.ContextKeyClaimsMapping = [];
        })).ConnectionResetRequired.Should().BeFalse();
    }
}
