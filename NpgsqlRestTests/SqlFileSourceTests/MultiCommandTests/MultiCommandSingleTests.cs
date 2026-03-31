using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests.SqlFileSourceTests;

public static partial class SqlFiles
{
    public static void MultiCommandSingleTests()
    {
        // Per-command @single on first command only
        File.WriteAllText(Path.Combine(Dir, "multi_single_first.sql"), """
            -- HTTP GET
            -- @param $1 id
            -- @single
            select id, name from sql_describe_test where id = $1;
            select id, name from sql_describe_test where id = $1;
            """);

        // Per-command @single on second command only
        File.WriteAllText(Path.Combine(Dir, "multi_single_second.sql"), """
            -- HTTP GET
            -- @param $1 id
            select id, name from sql_describe_test where id = $1;
            -- @single
            select id, name from sql_describe_test where id = $1;
            """);

        // Per-command @single on both commands
        File.WriteAllText(Path.Combine(Dir, "multi_single_both.sql"), """
            -- HTTP GET
            -- @param $1 id
            -- @single
            select id, name from sql_describe_test where id = $1;
            -- @single
            select id, name from sql_describe_test where id = $1;
            """);

        // Per-command @single with a void command in between
        File.WriteAllText(Path.Combine(Dir, "multi_single_with_void.sql"), """
            -- HTTP POST
            -- @param $1 id
            -- @single
            select id, name from sql_describe_test where id = $1;
            update sql_describe_test set active = true where id = $1;
            select count(*) as total from sql_describe_test;
            """);

        // Multi-command without @single for comparison
        File.WriteAllText(Path.Combine(Dir, "multi_no_single.sql"), """
            -- HTTP GET
            -- @param $1 id
            select id, name from sql_describe_test where id = $1;
            select id, name from sql_describe_test where id = $1;
            """);

        // Per-command @single with multi-row query — only first row returned
        File.WriteAllText(Path.Combine(Dir, "multi_single_multi_row.sql"), """
            -- HTTP GET
            select id, name from sql_describe_test order by id;
            -- @single
            select id, name from sql_describe_test order by id;
            """);

        // Positional @result combined with @single
        File.WriteAllText(Path.Combine(Dir, "multi_single_named.sql"), """
            -- HTTP GET
            -- @param $1 id
            -- @single
            -- @result user
            select id, name from sql_describe_test where id = $1;
            -- @result items
            select id, name from sql_describe_test order by id;
            """);

        // Positional @result without @single
        File.WriteAllText(Path.Combine(Dir, "multi_positional_result.sql"), """
            -- HTTP GET
            -- @param $1 id
            -- @result lookup
            select id, name from sql_describe_test where id = $1;
            -- @result all_items
            select id, name from sql_describe_test order by id;
            """);

        // Positional @result with "is" syntax
        File.WriteAllText(Path.Combine(Dir, "multi_positional_result_is.sql"), """
            -- HTTP GET
            -- @param $1 id
            -- @result is details
            select id, name from sql_describe_test where id = $1;
            select count(*) as total from sql_describe_test;
            """);

        // Both commands use positional @result
        File.WriteAllText(Path.Combine(Dir, "multi_mixed_result.sql"), """
            -- HTTP GET
            -- @param $1 id
            select id, name from sql_describe_test where id = $1;
            -- @result count
            select id, name from sql_describe_test order by id limit 1;
            """);
    }
}

[Collection("SqlFileSourceFixture")]
public class MultiCommandSingleTests(SqlFileSourceTestFixture test)
{
    [Fact]
    public async Task MultiSingleFirst_FirstIsObjectSecondIsArray()
    {
        using var response = await test.Client.GetAsync("/api/multi-single-first?id=1");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().Be("{\"result1\":{\"id\":1,\"name\":\"test1\"},\"result2\":[{\"id\":1,\"name\":\"test1\"}]}");
    }

    [Fact]
    public async Task MultiSingleSecond_FirstIsArraySecondIsObject()
    {
        using var response = await test.Client.GetAsync("/api/multi-single-second?id=1");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().Be("{\"result1\":[{\"id\":1,\"name\":\"test1\"}],\"result2\":{\"id\":1,\"name\":\"test1\"}}");
    }

    [Fact]
    public async Task MultiSingleBoth_BothAreObjects()
    {
        using var response = await test.Client.GetAsync("/api/multi-single-both?id=1");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().Be("{\"result1\":{\"id\":1,\"name\":\"test1\"},\"result2\":{\"id\":1,\"name\":\"test1\"}}");
    }

    [Fact]
    public async Task MultiSingleWithVoid_SingleObjectThenVoidThenArray()
    {
        using var response = await test.Client.PostAsync("/api/multi-single-with-void",
            new StringContent("{\"id\":1}", System.Text.Encoding.UTF8, "application/json"));
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");

        using var doc = JsonDocument.Parse(content);
        // result1: single object
        doc.RootElement.GetProperty("result1").ValueKind.Should().Be(JsonValueKind.Object);
        doc.RootElement.GetProperty("result1").GetProperty("id").GetInt32().Should().Be(1);
        // result2: rows affected (number)
        doc.RootElement.GetProperty("result2").ValueKind.Should().Be(JsonValueKind.Number);
        // result3: array
        doc.RootElement.GetProperty("result3").ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task MultiNoSingle_BothAreArrays()
    {
        using var response = await test.Client.GetAsync("/api/multi-no-single?id=1");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().Be("{\"result1\":[{\"id\":1,\"name\":\"test1\"}],\"result2\":[{\"id\":1,\"name\":\"test1\"}]}");
    }

    [Fact]
    public async Task MultiSingleMultiRow_SingleReturnsOnlyFirstRow()
    {
        using var response = await test.Client.GetAsync("/api/multi-single-multi-row");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");

        using var doc = JsonDocument.Parse(content);
        // result1: array (no @single)
        doc.RootElement.GetProperty("result1").ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetProperty("result1").GetArrayLength().Should().BeGreaterThan(1);
        // result2: single object (first row only)
        doc.RootElement.GetProperty("result2").ValueKind.Should().Be(JsonValueKind.Object);
        doc.RootElement.GetProperty("result2").GetProperty("id").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task MultiSingleNamed_PositionalResultWithSingle()
    {
        using var response = await test.Client.GetAsync("/api/multi-single-named?id=1");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");

        using var doc = JsonDocument.Parse(content);
        // "user" key with single object
        doc.RootElement.GetProperty("user").ValueKind.Should().Be(JsonValueKind.Object);
        doc.RootElement.GetProperty("user").GetProperty("id").GetInt32().Should().Be(1);
        // "items" key with array
        doc.RootElement.GetProperty("items").ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task MultiPositionalResult_CustomNames()
    {
        using var response = await test.Client.GetAsync("/api/multi-positional-result?id=1");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");

        using var doc = JsonDocument.Parse(content);
        doc.RootElement.TryGetProperty("lookup", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("all_items", out _).Should().BeTrue();
        // Should NOT have result1/result2
        doc.RootElement.TryGetProperty("result1", out _).Should().BeFalse();
        doc.RootElement.TryGetProperty("result2", out _).Should().BeFalse();
    }

    [Fact]
    public async Task MultiPositionalResultIs_IsSyntax()
    {
        using var response = await test.Client.GetAsync("/api/multi-positional-result-is?id=1");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");

        using var doc = JsonDocument.Parse(content);
        doc.RootElement.TryGetProperty("details", out _).Should().BeTrue();
        // Second result uses default name (no positional annotation)
        doc.RootElement.TryGetProperty("result2", out _).Should().BeTrue();
    }

    [Fact]
    public async Task MultiMixedResult_BothPositional()
    {
        using var response = await test.Client.GetAsync("/api/multi-mixed-result?id=1");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");

        using var doc = JsonDocument.Parse(content);
        // First result: no annotation → default "result1"
        doc.RootElement.TryGetProperty("result1", out _).Should().BeTrue();
        // Second result: positional @result count
        doc.RootElement.TryGetProperty("count", out _).Should().BeTrue();
    }
}
