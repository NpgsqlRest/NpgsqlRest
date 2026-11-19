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

        create procedure crr_proc_user_context() language plpgsql as $$ begin perform current_setting('request.user_id', true); end $$;
        comment on procedure crr_proc_user_context() is 'HTTP POST
        user_context';

        create function crr_bytes_user_context() returns bytea language sql stable as $$ select convert_to(current_setting('request.user_id', true), 'utf8') $$;
        comment on function crr_bytes_user_context() is 'HTTP GET
        user_context';
        """);
    }
}

/// <summary>
/// NpgsqlRestOptions.ConnectionResetRequired tells the host whether a request can leave session-level settings on its
/// connection - that is, whether pooled connections have to be reset between requests. UseNpgsqlRest sets it from
/// the endpoints it builds.
///
/// The request context of functions, tables, views and single-statement SQL files is sent together with the routine,
/// as settings local to that one batch (RequestContextBatch), so it leaves nothing behind. What still does: the
/// context of procedures and of the few other endpoints that cannot use the batch, BeforeRoutineCommands, and uploads
/// with a metadata context key.
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

        // UseNpgsqlRest makes its options and logger the current ones for the process. These apps never serve a
        // request, so the ones of the app that does are put back.
        var currentOptions = NpgsqlRestOptions.Options;
        var currentLogger = NpgsqlRestOptions.Logger;
        try
        {
            app.UseNpgsqlRest(options);
        }
        finally
        {
            NpgsqlRestOptions.Options = currentOptions;
            NpgsqlRestOptions.Logger = currentLogger;
        }
        return options;
    }

    [Fact]
    public async Task Not_required_when_no_endpoint_sets_a_context()
    {
        (await BuildAsync("crr[_]plain")).ConnectionResetRequired.Should().BeFalse();
    }

    [Fact]
    public async Task Not_required_when_user_context_is_sent_with_the_function()
    {
        (await BuildAsync("crr[_](plain|user[_]context)")).ConnectionResetRequired.Should().BeFalse();
    }

    [Fact]
    public async Task Not_required_when_request_headers_context_is_sent_with_the_function()
    {
        (await BuildAsync("crr[_](plain|headers[_]context)")).ConnectionResetRequired.Should().BeFalse();
    }

    [Fact]
    public async Task Required_when_a_procedure_uses_user_context()
    {
        // a procedure can commit, which would end local settings: its context stays at session level
        (await BuildAsync("crr[_](plain|user[_]context|proc[_]user[_]context)")).ConnectionResetRequired.Should().BeTrue();
    }

    [Fact]
    public async Task Required_when_a_function_returning_bytea_uses_user_context()
    {
        // read as binary, which a batch with text results cannot do: its context stays at session level
        (await BuildAsync("crr[_](plain|bytes[_]user[_]context)")).ConnectionResetRequired.Should().BeTrue();
    }

    [Fact]
    public async Task Required_when_a_command_callback_is_set_and_an_endpoint_uses_user_context()
    {
        // the callback works with the routine's command and may run it itself: the context is set before it
        (await BuildAsync("crr[_](plain|user[_]context)", o => o.CommandCallbackAsync = (_, _, _) => Task.CompletedTask))
            .ConnectionResetRequired.Should().BeTrue();
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
        (await BuildAsync("crr[_](plain|user[_]context|headers[_]context|proc[_]user[_]context)", o =>
        {
            o.WrapInTransaction = true;
            o.BeforeRoutineCommands = ["select set_config('app.tenant', 'x', true)"];
        })).ConnectionResetRequired.Should().BeFalse();
    }

    [Fact]
    public async Task Not_required_when_user_context_has_no_context_keys_to_set()
    {
        (await BuildAsync("crr[_](plain|user[_]context|proc[_]user[_]context)", o =>
        {
            o.AuthenticationOptions.IpAddressContextKey = null;
            o.AuthenticationOptions.ClaimsJsonContextKey = null;
            o.AuthenticationOptions.ContextKeyClaimsMapping = [];
        })).ConnectionResetRequired.Should().BeFalse();
    }
}
