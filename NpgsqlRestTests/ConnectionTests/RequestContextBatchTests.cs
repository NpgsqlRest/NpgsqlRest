using System.Net;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void RequestContextBatchTests()
    {
        script.Append("""
        -- What is set on the connection right now, without setting anything: "<none>" for a setting that is not set.
        create function rcb_peek() returns text language sql as $$
        select
            coalesce(nullif(current_setting('request.user_id', true), ''), '<none>') || '|' ||
            coalesce(nullif(current_setting('request.user_name', true), ''), '<none>') || '|' ||
            coalesce(nullif(current_setting('request.ip_address', true), ''), '<none>') || '|' ||
            coalesce(nullif(current_setting('request.headers', true), ''), '<none>')
        $$;
        comment on function rcb_peek() is 'HTTP GET';

        create function rcb_scalar(_p text) returns text language sql as $$
        select _p || ':' ||
            coalesce(nullif(current_setting('request.user_id', true), ''), '<none>') || '|' ||
            coalesce(nullif(current_setting('request.user_name', true), ''), '<none>') || '|' ||
            (nullif(current_setting('request.ip_address', true), '') is not null)::text
        $$;
        comment on function rcb_scalar(text) is 'HTTP GET
        user_context';

        create function rcb_rows(_n int) returns table (n int, user_id text, flag boolean, amount numeric, at timestamp, tags text[], doc json, code char(4))
        language sql as $$
        select i, current_setting('request.user_id', true), i % 2 = 0, i * 1.5, timestamp '2026-01-02 03:04:05' + make_interval(days => i),
            array['a', 'b ' || i], json_build_object('i', i), 'x'::char(4)
        from generate_series(1, _n) i
        $$;
        comment on function rcb_rows(int) is 'HTTP GET
        user_context';

        -- the same rows without the request context: rendering must not depend on how the routine is sent
        create function rcb_rows_plain(_n int) returns table (n int, user_id text, flag boolean, amount numeric, at timestamp, tags text[], doc json, code char(4))
        language sql as $$
        select i, '123'::text, i % 2 = 0, i * 1.5, timestamp '2026-01-02 03:04:05' + make_interval(days => i),
            array['a', 'b ' || i], json_build_object('i', i), 'x'::char(4)
        from generate_series(1, _n) i
        $$;
        comment on function rcb_rows_plain(int) is 'HTTP GET';

        create table rcb_log (v text primary key, user_id text);
        create function rcb_void(_v text) returns void language sql as $$
        insert into rcb_log values (_v, current_setting('request.user_id', true))
        $$;
        comment on function rcb_void(text) is 'HTTP POST
        user_context';
        create function rcb_log_get(_v text) returns text language sql as $$ select user_id from rcb_log where v = _v $$;
        comment on function rcb_log_get(text) is 'HTTP GET';

        create function rcb_headers() returns text language sql as $$
        select value from json_each_text(current_setting('request.headers', true)::json) where lower(key) = 'x-rcb-test'
        $$;
        comment on function rcb_headers() is 'HTTP GET
        request_headers context';

        create function rcb_both() returns text language sql as $$
        select current_setting('request.user_id', true) || '|' ||
            (select value from json_each_text(current_setting('request.headers', true)::json) where lower(key) = 'x-rcb-test')
        $$;
        comment on function rcb_both() is 'HTTP GET
        user_context
        request_headers context';

        create function rcb_fail() returns text language plpgsql as $$
        begin
            raise exception 'failed for %', current_setting('request.user_id', true);
        end $$;
        comment on function rcb_fail() is 'HTTP GET
        user_context';

        -- Each counts its attempts in a sequence (sequences are not rolled back) and fails the first one with a
        -- retryable error (40001, serialization_failure). The default retry strategy retries at once.
        create sequence rcb_retry_scalar_seq;
        create function rcb_retry_scalar() returns text language plpgsql as $$
        begin
            if nextval('rcb_retry_scalar_seq') = 1 then
                raise exception 'could not serialize' using errcode = '40001';
            end if;
            return 'attempt ' || currval('rcb_retry_scalar_seq') || ' for ' || current_setting('request.user_id', true);
        end $$;
        comment on function rcb_retry_scalar() is 'HTTP GET
        user_context';

        create sequence rcb_retry_rows_seq;
        create function rcb_retry_rows() returns table (attempt bigint, user_id text) language plpgsql as $$
        begin
            if nextval('rcb_retry_rows_seq') = 1 then
                raise exception 'could not serialize' using errcode = '40001';
            end if;
            return query select currval('rcb_retry_rows_seq'), current_setting('request.user_id', true);
        end $$;
        comment on function rcb_retry_rows() is 'HTTP GET
        user_context';

        create sequence rcb_retry_void_seq;
        create function rcb_retry_void() returns void language plpgsql as $$
        begin
            if nextval('rcb_retry_void_seq') = 1 then
                raise exception 'could not serialize' using errcode = '40001';
            end if;
            insert into rcb_log values ('retry-void attempt ' || currval('rcb_retry_void_seq'), current_setting('request.user_id', true));
        end $$;
        comment on function rcb_retry_void() is 'HTTP POST
        user_context';

        create sequence rcb_cached_seq;
        create function rcb_cached(_key text) returns text language sql as $$
        select _key || ':' || current_setting('request.user_id', true) || ':' || nextval('rcb_cached_seq')
        $$;
        comment on function rcb_cached(text) is 'HTTP GET
        user_context
        cached _key';

        """);
    }
}

/// <summary>
/// The request context (user_context, request_headers context) of functions is sent to PostgreSQL together with the
/// routine, in one round trip, as settings that are local to that one batch. The routine sees them, and nothing stays
/// on the connection - which these tests can observe because the fixture's only connection is never reset.
/// </summary>
[Collection("RequestContextBatchTestFixture")]
public class RequestContextBatchTests(RequestContextBatchTestFixture test)
{
    private const string NothingSet = "<none>|<none>|<none>|<none>";

    private static async Task<string> GetAsync(HttpClient client, string url, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await client.GetAsync(url);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(expected, content);
        return content;
    }

    [Fact]
    public async Task Scalar_sees_the_context_and_nothing_stays_on_the_connection()
    {
        using var user = await test.LoggedInClientAsync();

        (await GetAsync(user, "/api/rcb-scalar/?p=a")).Should().Be("a:123|admin|true");
        // the same PostgreSQL session, not reset in between
        (await GetAsync(user, "/api/rcb-peek/")).Should().Be(NothingSet);
    }

    [Fact]
    public async Task Anonymous_request_gets_the_ip_address_and_no_user()
    {
        using var user = await test.LoggedInClientAsync();
        using var anonymous = test.AnonymousClient();

        (await GetAsync(user, "/api/rcb-scalar/?p=b")).Should().Be("b:123|admin|true");
        (await GetAsync(anonymous, "/api/rcb-scalar/?p=c")).Should().Be("c:<none>|<none>|true");
        (await GetAsync(anonymous, "/api/rcb-peek/")).Should().Be(NothingSet);
    }

    [Fact]
    public async Task Rows_see_the_context_and_render_exactly_as_without_it()
    {
        using var user = await test.LoggedInClientAsync();

        var expected =
            "[{\"n\":1,\"userId\":\"123\",\"flag\":false,\"amount\":1.5,\"at\":\"2026-01-03T03:04:05\",\"tags\":[\"a\",\"b 1\"],\"doc\":{\"i\" : 1},\"code\":\"x   \"}," +
            "{\"n\":2,\"userId\":\"123\",\"flag\":true,\"amount\":3.0,\"at\":\"2026-01-04T03:04:05\",\"tags\":[\"a\",\"b 2\"],\"doc\":{\"i\" : 2},\"code\":\"x   \"}]";
        (await GetAsync(user, "/api/rcb-rows/?n=2")).Should().Be(expected);
        (await GetAsync(user, "/api/rcb-rows-plain/?n=2")).Should().Be(expected);
        (await GetAsync(user, "/api/rcb-peek/")).Should().Be(NothingSet);
    }

    [Fact]
    public async Task Many_rows_are_complete()
    {
        using var user = await test.LoggedInClientAsync();

        var withContext = await GetAsync(user, "/api/rcb-rows/?n=3000");
        var plain = await GetAsync(user, "/api/rcb-rows-plain/?n=3000");
        withContext.Should().Be(plain);
        withContext.Should().StartWith("[{\"n\":1,\"userId\":\"123\",");
        withContext.Should().EndWith("{\"n\":3000,\"userId\":\"123\",\"flag\":true,\"amount\":4500.0,\"at\":\"2034-03-21T03:04:05\",\"tags\":[\"a\",\"b 3000\"],\"doc\":{\"i\" : 3000},\"code\":\"x   \"}]");
    }

    [Fact]
    public async Task Void_routine_sees_the_context()
    {
        using var user = await test.LoggedInClientAsync();

        using (var body = new StringContent("{\"v\":\"void-1\"}", Encoding.UTF8, "application/json"))
        using (var response = await user.PostAsync("/api/rcb-void/", body))
        {
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }
        (await GetAsync(user, "/api/rcb-log-get/?v=void-1")).Should().Be("123");
        (await GetAsync(user, "/api/rcb-peek/")).Should().Be(NothingSet);
    }

    [Fact]
    public async Task Request_headers_context_is_sent_with_the_routine()
    {
        using var client = test.AnonymousClient();
        client.DefaultRequestHeaders.Add("X-Rcb-Test", "header value");

        (await GetAsync(client, "/api/rcb-headers/")).Should().Be("header value");
        (await GetAsync(client, "/api/rcb-peek/")).Should().Be(NothingSet);
    }

    [Fact]
    public async Task User_context_and_request_headers_context_together()
    {
        using var user = await test.LoggedInClientAsync();
        user.DefaultRequestHeaders.Add("X-Rcb-Test", "both");

        (await GetAsync(user, "/api/rcb-both/")).Should().Be("123|both");
        (await GetAsync(user, "/api/rcb-peek/")).Should().Be(NothingSet);
    }

    [Fact]
    public async Task Failing_routine_reports_its_error_and_the_connection_stays_usable()
    {
        using var user = await test.LoggedInClientAsync();

        (await GetAsync(user, "/api/rcb-fail/", HttpStatusCode.BadRequest))
            .Should().Be("{\"type\":\"https://tools.ietf.org/html/rfc9110#section-15.5.1\",\"title\":\"failed for 123\",\"status\":400,\"detail\":\"P0001\"}");
        // the only connection: still usable, and clean
        (await GetAsync(user, "/api/rcb-peek/")).Should().Be(NothingSet);
        (await GetAsync(user, "/api/rcb-scalar/?p=after")).Should().Be("after:123|admin|true");
    }

    [Fact]
    public async Task Scalar_is_retried_with_its_context()
    {
        using var user = await test.LoggedInClientAsync();

        (await GetAsync(user, "/api/rcb-retry-scalar/")).Should().Be("attempt 2 for 123");
        (await GetAsync(user, "/api/rcb-peek/")).Should().Be(NothingSet);
    }

    [Fact]
    public async Task Rows_are_retried_with_their_context()
    {
        using var user = await test.LoggedInClientAsync();

        (await GetAsync(user, "/api/rcb-retry-rows/")).Should().Be("[{\"attempt\":2,\"userId\":\"123\"}]");
        (await GetAsync(user, "/api/rcb-peek/")).Should().Be(NothingSet);
    }

    [Fact]
    public async Task Void_routine_is_retried_with_its_context()
    {
        using var user = await test.LoggedInClientAsync();

        using (var response = await user.PostAsync("/api/rcb-retry-void/", null))
        {
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }
        // the first attempt was rolled back as a whole; the second one wrote the row
        (await GetAsync(user, "/api/rcb-log-get/?v=retry-void attempt 2")).Should().Be("123");
        (await GetAsync(user, "/api/rcb-log-get/?v=retry-void attempt 1")).Should().Be("");
        (await GetAsync(user, "/api/rcb-peek/")).Should().Be(NothingSet);
    }

    [Fact]
    public async Task Cached_value_is_computed_with_the_context_once()
    {
        using var user = await test.LoggedInClientAsync();

        (await GetAsync(user, "/api/rcb-cached/?key=k")).Should().Be("k:123:1");
        // from the cache: the routine did not run again
        (await GetAsync(user, "/api/rcb-cached/?key=k")).Should().Be("k:123:1");
        (await GetAsync(user, "/api/rcb-peek/")).Should().Be(NothingSet);
    }

    [Fact]
    public async Task Concurrent_requests_each_see_their_own_context()
    {
        using var user = await test.LoggedInClientAsync();
        using var anonymous = test.AnonymousClient();

        var tasks = new List<Task<string>>();
        for (var i = 0; i < 40; i++)
        {
            tasks.Add(GetAsync(i % 2 == 0 ? user : anonymous, "/api/rcb-scalar/?p=" + i));
        }
        var results = await Task.WhenAll(tasks);
        for (var i = 0; i < results.Length; i++)
        {
            results[i].Should().Be(i % 2 == 0 ? $"{i}:123|admin|true" : $"{i}:<none>|<none>|true");
        }
        (await GetAsync(user, "/api/rcb-peek/")).Should().Be(NothingSet);
    }

    [Fact]
    public void No_endpoint_here_requires_the_connection_reset()
    {
        // every endpoint of this fixture sends its context with the routine, so nothing can stay on a connection
        test.Options.ConnectionResetRequired.Should().BeFalse();
    }
}
