namespace NpgsqlRestTests;

public static partial class Database
{
    public static void CommandRetryTests()
    {
        script.Append("""
        -- Each routine counts its attempts in a sequence (sequences are not rolled back) and fails the first one
        -- with a retryable error (40001, serialization_failure). The default retry strategy retries at once.
        create sequence retry_scalar_seq;
        create function retry_scalar() returns text language plpgsql as $$
        begin
            if nextval('retry_scalar_seq') = 1 then
                raise exception 'could not serialize' using errcode = '40001';
            end if;
            return 'ok on attempt ' || currval('retry_scalar_seq');
        end $$;
        comment on function retry_scalar() is 'HTTP GET';

        create sequence retry_rows_seq;
        create function retry_rows() returns table (attempt bigint, name text) language plpgsql as $$
        begin
            if nextval('retry_rows_seq') = 1 then
                raise exception 'could not serialize' using errcode = '40001';
            end if;
            return query select currval('retry_rows_seq'), 'row'::text;
        end $$;
        comment on function retry_rows() is 'HTTP GET';

        create sequence retry_void_seq;
        create function retry_void() returns void language plpgsql as $$
        begin
            if nextval('retry_void_seq') = 1 then
                raise exception 'could not serialize' using errcode = '40001';
            end if;
        end $$;
        comment on function retry_void() is 'HTTP POST';
        create function retry_void_attempts() returns text language sql as $$ select last_value::text from retry_void_seq $$;
        comment on function retry_void_attempts() is 'HTTP GET';

        -- not a retryable error: one attempt only
        create sequence retry_none_seq;
        create function retry_not_retryable() returns text language plpgsql as $$
        begin
            perform nextval('retry_none_seq');
            perform 1 / (currval('retry_none_seq') - currval('retry_none_seq'));
            return 'unreachable';
        end $$;
        comment on function retry_not_retryable() is 'HTTP GET';
        create function retry_not_retryable_attempts() returns text language sql as $$ select last_value::text from retry_none_seq $$;
        comment on function retry_not_retryable_attempts() is 'HTTP GET';
        """);
    }
}

/// <summary>
/// Command retries (CommandRetryOptions, default strategy): a command that fails with a retryable error is executed
/// again. The first attempt is made by the request itself and the retry logic takes over when it fails, so these
/// cover that hand-over for each kind of result.
/// </summary>
[Collection("TestFixture")]
public class CommandRetryTests(TestFixture test)
{
    [Fact]
    public async Task Scalar_is_retried_after_a_retryable_error()
    {
        using var response = await test.Client.GetAsync("/api/retry-scalar/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("ok on attempt 2");
    }

    [Fact]
    public async Task Rows_are_retried_after_a_retryable_error()
    {
        using var response = await test.Client.GetAsync("/api/retry-rows/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("[{\"attempt\":2,\"name\":\"row\"}]");
    }

    [Fact]
    public async Task Void_is_retried_after_a_retryable_error()
    {
        using var response = await test.Client.PostAsync("/api/retry-void/", null);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var attempts = await test.Client.GetAsync("/api/retry-void-attempts/");
        (await attempts.Content.ReadAsStringAsync()).Should().Be("2");
    }

    [Fact]
    public async Task Error_that_is_not_retryable_is_returned_after_one_attempt()
    {
        using var response = await test.Client.GetAsync("/api/retry-not-retryable/");
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await response.Content.ReadAsStringAsync()).Should().Be("{\"title\":\"22012: division by zero\",\"status\":500,\"detail\":\"22012\"}");

        using var attempts = await test.Client.GetAsync("/api/retry-not-retryable-attempts/");
        (await attempts.Content.ReadAsStringAsync()).Should().Be("1");
    }
}
