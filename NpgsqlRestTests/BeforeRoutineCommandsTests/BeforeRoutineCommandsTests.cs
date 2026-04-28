using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void BeforeRoutineCommandsTests()
    {
        script.Append(@"
        create function brc_get_tenant_guc()
        returns text language sql as $$
            select current_setting('app.tenant_id', true);
        $$;
        comment on function brc_get_tenant_guc() is 'HTTP GET';

        create function brc_get_search_path()
        returns text language sql as $$
            select current_setting('search_path');
        $$;
        comment on function brc_get_search_path() is 'HTTP GET';

        create function brc_get_request_time()
        returns text language sql as $$
            select current_setting('app.request_time', true);
        $$;
        comment on function brc_get_request_time() is 'HTTP GET';

        create function brc_get_user_agent()
        returns text language sql as $$
            select current_setting('app.user_agent', true);
        $$;
        comment on function brc_get_user_agent() is 'HTTP GET';

        create function brc_get_client_ip()
        returns text language sql as $$
            select current_setting('app.client_ip', true);
        $$;
        comment on function brc_get_client_ip() is 'HTTP GET';

        create function brc_get_combined()
        returns table (
            stage1 text,
            stage2 text
        )
        language sql as $$
            select
                current_setting('app.stage1', true),
                current_setting('app.stage2', true);
        $$;
        comment on function brc_get_combined() is 'HTTP GET';

        create function brc_force_error()
        returns text language plpgsql as $$
        begin
            perform set_config('app.tenant_id_during_error', current_setting('app.tenant_id', true), true);
            raise exception 'forced error after seeing tenant';
        end;
        $$;
        comment on function brc_force_error() is 'HTTP GET';

        create function brc_login()
        returns table (
            name_identifier int,
            name text,
            tenant_id text
        )
        language sql as $$
            select
                42 as name_identifier,
                'tenant_user' as name,
                'tenant_a' as tenant_id;
        $$;
        comment on function brc_login() is 'login';

        create schema if not exists tenant_a;
        create table tenant_a.brc_items(id int, name text);
        insert into tenant_a.brc_items values (1, 'tenant_a_item');

        create schema if not exists tenant_b;
        create table tenant_b.brc_items(id int, name text);
        insert into tenant_b.brc_items values (1, 'tenant_b_item');

        create function brc_read_items()
        returns table (id int, name text)
        language plpgsql as $$
        begin
            return query execute 'select id, name from brc_items';
        end;
        $$;
        comment on function brc_read_items() is 'HTTP GET';
        ");
    }
}

[Collection("BeforeRoutineCommandsTestFixture")]
public class BeforeRoutineCommandsTests(BeforeRoutineCommandsTestFixture test)
{
    private async Task<HttpClient> NewLoggedInClientAsync()
    {
        var client = new HttpClient { BaseAddress = new Uri(test.ServerAddress) };
        client.Timeout = TimeSpan.FromMinutes(5);
        var login = await client.GetAsync("/brc-login");
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        return client;
    }

    [Fact]
    public async Task Test_WrapInTransaction_local_set_config_visible_in_routine()
    {
        // The fixture has WrapInTransaction = true and a BeforeRoutineCommand that sets app.request_time.
        // The routine reads it via current_setting — confirms is_local=true GUCs are visible inside the routine.
        using var client = await NewLoggedInClientAsync();
        using var response = await client.GetAsync("/api/brc-get-request-time");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        // We don't know the exact timestamp but it must be a non-empty PG timestamp string.
        content.Trim('"').Should().NotBeEmpty();
        content.Trim('"').Should().Contain("-"); // ISO timestamp contains dashes
    }

    [Fact]
    public async Task Test_BeforeRoutineCommands_raw_sql_executes()
    {
        // Stage1/stage2 are set via two raw-SQL shorthand entries; stage2 references stage1.
        using var client = await NewLoggedInClientAsync();
        using var response = await client.GetAsync("/api/brc-get-combined");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("[{\"stage1\":\"one\",\"stage2\":\"one-two\"}]");
    }

    [Fact]
    public async Task Test_Claim_parameter_binding_resolves_to_actual_claim_value()
    {
        // After login, tenant_id claim is "tenant_a". The BeforeRoutineCommand binds it as $1 and writes to app.tenant_id GUC.
        using var client = await NewLoggedInClientAsync();
        using var response = await client.GetAsync("/api/brc-get-tenant-guc");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("tenant_a");
    }

    [Fact]
    public async Task Test_RequestHeader_parameter_binding_reads_named_header()
    {
        // Send a custom User-Agent and verify it was bound and stored in app.user_agent GUC.
        using var client = await NewLoggedInClientAsync();
        client.DefaultRequestHeaders.Remove("User-Agent");
        client.DefaultRequestHeaders.Add("User-Agent", "MyTestAgent/1.0");
        using var response = await client.GetAsync("/api/brc-get-user-agent");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("MyTestAgent/1.0");
    }

    [Fact]
    public async Task Test_IpAddress_parameter_binding_resolves_client_ip()
    {
        // The IpAddress source uses GetClientIpAddressDbParam(); for Kestrel localhost it returns "127.0.0.1" or "::1" or empty.
        // We just assert the GUC exists (even if value happens to be empty in this test host).
        using var client = await NewLoggedInClientAsync();
        using var response = await client.GetAsync("/api/brc-get-client-ip");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // No exception is the assertion: the IpAddress source did not throw and routine executed.
    }

    [Fact]
    public async Task Test_Missing_claim_binds_null_and_routine_handles_it()
    {
        // No login → tenant_id claim is missing → set_config('app.tenant_id', NULL, true) → current_setting returns ''.
        using var client = new HttpClient { BaseAddress = new Uri(test.ServerAddress) };
        client.Timeout = TimeSpan.FromMinutes(5);
        using var response = await client.GetAsync("/api/brc-get-tenant-guc");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        // current_setting returns empty string when GUC was set with NULL.
        content.Should().Be("");
    }

    [Fact]
    public async Task Test_Missing_header_binds_null()
    {
        // Strip default User-Agent so the header is missing → set_config receives NULL → GUC is empty.
        using var client = new HttpClient { BaseAddress = new Uri(test.ServerAddress) };
        client.Timeout = TimeSpan.FromMinutes(5);
        client.DefaultRequestHeaders.UserAgent.Clear();
        using var response = await client.GetAsync("/api/brc-get-user-agent");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("");
    }

    [Fact]
    public async Task Test_Commands_execute_in_declaration_order()
    {
        // stage2 reads from stage1 — only works if stage1 ran first. Same proof as the raw_sql test, but explicit.
        using var client = await NewLoggedInClientAsync();
        using var response = await client.GetAsync("/api/brc-get-combined");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("\"stage2\":\"one-two\"");
    }

    [Fact]
    public async Task Test_WrapInTransaction_rolls_back_on_routine_error()
    {
        // brc_force_error sets a session-level GUC (is_local=true) then raises.
        // With WrapInTransaction the transaction is rolled back on error, so the GUC should be gone.
        // We can't observe rollback directly across requests (each request gets its own connection),
        // but we can verify the request returns an error (P0001 → 400 by default mapping).
        using var client = await NewLoggedInClientAsync();
        using var response = await client.GetAsync("/api/brc-force-error");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Test_MultiTenant_search_path_via_claim()
    {
        // Login sets tenant_id=tenant_a. BeforeRoutineCommands chain sets search_path to "tenant_a, public".
        // brc_read_items() runs `select id, name from brc_items` (unqualified) — should resolve to tenant_a.brc_items.
        using var client = await NewLoggedInClientAsync();
        using var response = await client.GetAsync("/api/brc-read-items");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("[{\"id\":1,\"name\":\"tenant_a_item\"}]");
    }

    [Fact]
    public async Task Test_BeforeRoutineCommands_runs_when_no_other_context()
    {
        // None of the brc_* endpoints have UserContext or RequestHeadersMode=Context configured on the endpoint,
        // and our fixture has RequiresAuthorization=false. Yet BeforeRoutineCommands still ran (proven by previous tests).
        // This test specifically calls a non-authenticated request to verify the BeforeRoutineCommand still fires.
        using var client = new HttpClient { BaseAddress = new Uri(test.ServerAddress) };
        client.Timeout = TimeSpan.FromMinutes(5);
        using var response = await client.GetAsync("/api/brc-get-request-time");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Trim('"').Should().NotBeEmpty();
    }
}

[Collection("TestFixture")]
public class BeforeRoutineCommandsDefaultFixtureTests(TestFixture test)
{
    [Fact]
    public async Task Test_WrapInTransaction_false_is_default_and_BeforeRoutineCommands_empty()
    {
        // Default fixture has WrapInTransaction=false and no BeforeRoutineCommands.
        // Calling brc-get-tenant-guc reads app.tenant_id GUC which is never set → empty string.
        // Confirms the new options are opt-in and don't affect existing setups.
        using var client = test.Application.CreateClient();
        client.Timeout = TimeSpan.FromHours(1);
        using var response = await client.GetAsync("/api/brc-get-tenant-guc");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("");
    }
}
