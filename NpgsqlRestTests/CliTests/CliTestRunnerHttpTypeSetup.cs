namespace NpgsqlRestTests;

public static partial class Database
{
    public static void CliTestRunnerHttpTypeTests()
    {
        script.Append("""
        create schema if not exists trt;

        create type trt.outbound_call as (body text, status_code int);
        comment on type trt.outbound_call is 'GET http://127.0.0.1:9/never';
        create function trt.outbound(_req trt.outbound_call = null) returns text language sql as $$ select (_req).body $$;
        comment on function trt.outbound(trt.outbound_call) is 'HTTP GET';

        create function trt.ping() returns text language sql as $$ select 'pong' $$;
        comment on function trt.ping() is 'HTTP GET';
        create type trt.own_call as (body text, status_code int);
        comment on type trt.own_call is 'GET /api/trt/ping';
        create function trt.own(_req trt.own_call = null) returns text language sql as $$ select (_req).body $$;
        comment on function trt.own(trt.own_call) is 'HTTP GET';
        """);
    }
}
