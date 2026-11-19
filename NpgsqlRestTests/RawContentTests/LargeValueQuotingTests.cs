namespace NpgsqlRestTests;

public static partial class Database
{
    public static void LargeValueQuotingTests()
    {
        script.Append(
        """
        create function large_value_csv(_len int)
        returns table(t text)
        language sql
        stable
        as
        $$
        select repeat('x', _len)
        $$;
        comment on function large_value_csv(int) is '
        raw
        separator ,
        new_line \n
        Content-Type: text/csv
        ';

        create function large_value_varbit_json(_len int)
        returns table(b varbit)
        language sql
        stable
        as
        $$
        select repeat('1', _len)::varbit
        $$;
        """);
    }
}

/// <summary>
/// Security regression: quoting a value whose length comes from data must not use an unbounded stack buffer.
/// A multi-megabyte stored value used to overflow the thread stack (stackalloc of value length), which
/// terminates the whole server process - it cannot be caught.
/// </summary>
[Collection("TestFixture")]
public class LargeValueQuotingTests(TestFixture test)
{
    private const int Length = 3_000_000;

    [Fact]
    public async Task Large_text_value_in_csv_output_does_not_crash()
    {
        using var client = test.Application.CreateClient();
        client.Timeout = TimeSpan.FromHours(1);

        using var response = await client.GetAsync($"/api/large-value-csv/?len={Length}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be(string.Concat("\"", new string('x', Length), "\""));
    }

    [Fact]
    public async Task Large_varbit_value_in_json_output_does_not_crash()
    {
        using var client = test.Application.CreateClient();
        client.Timeout = TimeSpan.FromHours(1);

        using var response = await client.GetAsync($"/api/large-value-varbit-json/?len={Length}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be(string.Concat("[{\"b\":\"", new string('1', Length), "\"}]"));
    }
}
