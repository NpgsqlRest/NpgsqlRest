namespace NpgsqlRestTests.ParserTests;

public class FormatStringTests
{
    [Fact]
    public void Parse_simple()
    {
        Formatter.FormatString("", []).ToString().Should().Be("");

        var str5 = GenerateRandomString(5);
        Formatter.FormatString(str5.AsSpan(), []).ToString().Should().Be(str5);

        var str10 = GenerateRandomString(10);
        Formatter.FormatString(str10.AsSpan(), []).ToString().Should().Be(str10);

        var str50 = GenerateRandomString(50);
        Formatter.FormatString(str50.AsSpan(), []).ToString().Should().Be(str50);

        var str100 = GenerateRandomString(100);
        Formatter.FormatString(str100.AsSpan(), []).ToString().Should().Be(str100);
    }

    [Fact]
    public void Parse_one()
    {
        var str1 = "Hello, {name}!";
        Formatter.FormatString(str1.AsSpan(), new Dictionary<string, string> { { "name", "world" } }).ToString().Should().Be("Hello, world!");
    }

    [Fact]
    public void Parse_two()
    {
        var str1 = "Hello, {name1} and {name2}!";
        Formatter.FormatString(str1.AsSpan(), new Dictionary<string, string> { 
            { "name1", "world1" }, { "name2", "world2" } }).ToString().Should().Be("Hello, world1 and world2!");
    }

    [Fact]
    public void Parse_multiple_placeholders()
    {
        var str = "Hello, {name}! Today is {day}.";
        var replacements = new Dictionary<string, string>
        {
            { "name", "Alice" },
            { "day", "Monday" }
        };
        Formatter.FormatString(str.AsSpan(), replacements).ToString().Should().Be("Hello, Alice! Today is Monday.");
    }

    [Fact]
    public void Parse_placeholder_not_found()
    {
        var str = "Hello, {name}!";
        Formatter.FormatString(str.AsSpan(), []).ToString().Should().Be("Hello, {name}!");
    }

    [Fact]
    public void Parse_placeholder_with_special_characters()
    {
        var str = "Hello, {user_name}!";
        var replacements = new Dictionary<string, string>
        {
            { "user_name", "user@domain.com" }
        };
        Formatter.FormatString(str.AsSpan(), replacements).ToString().Should().Be("Hello, user@domain.com!");
    }

    [Fact]
    public void Parse_placeholder_with_numbers()
    {
        var str = "Your order number is {order_number}.";
        var replacements = new Dictionary<string, string>
        {
            { "order_number", "12345" }
        };
        Formatter.FormatString(str.AsSpan(), replacements).ToString().Should().Be("Your order number is 12345.");
    }

    [Fact]
    public void Parse_empty_placeholder()
    {
        var str = "Hello, {}!";
        Formatter.FormatString(str.AsSpan(), []).ToString().Should().Be("Hello, {}!");
    }

    [Fact]
    public void Parse_null_replacements()
    {
        var str = "Hello, {name}!";
        Formatter.FormatString(str.AsSpan(), null!).ToString().Should().Be("Hello, {name}!");
    }

    [Fact]
    public void Parse_repeated_placeholders()
    {
        var str = "{greeting}, {greeting}!";
        var replacements = new Dictionary<string, string>
        {
            { "greeting", "Hi" }
        };
        Formatter.FormatString(str.AsSpan(), replacements).ToString().Should().Be("Hi, Hi!");
    }

    [Fact]
    public void Parse_placeholder_with_empty_value()
    {
        var str = "Hello, {name}!";
        var replacements = new Dictionary<string, string>
        {
            { "name", "" }
        };
        Formatter.FormatString(str.AsSpan(), replacements).ToString().ToString().Should().Be("Hello, !");
    }

    [Fact]
    public void Parse_ten()
    {
        var str = $"{GenerateRandomString(10)}{{name1}}{GenerateRandomString(10)}{{name2}}{GenerateRandomString(10)}{{name3}}{GenerateRandomString(10)}{{name4}}{GenerateRandomString(10)}{{name5}}{GenerateRandomString(10)}{{name6}}{GenerateRandomString(10)}{{name7}}{GenerateRandomString(10)}{{name8}}{GenerateRandomString(10)}{{name9}}{GenerateRandomString(10)}{{name10}}{GenerateRandomString(10)}";
        var replacements = new Dictionary<string, string>
    {
        { "name1", "value1" },
        { "name2", "value2" },
        { "name3", "value3" },
        { "name4", "value4" },
        { "name5", "value5" },
        { "name6", "value6" },
        { "name7", "value7" },
        { "name8", "value8" },
        { "name9", "value9" },
        { "name10", "value10" }
    };
        Formatter.FormatString(str.AsSpan(), replacements)
            .ToString().Should().Be(str
                .Replace("{name1}", "value1")
                .Replace("{name2}", "value2")
                .Replace("{name3}", "value3")
                .Replace("{name4}", "value4")
                .Replace("{name5}", "value5")
                .Replace("{name6}", "value6")
                .Replace("{name7}", "value7")
                .Replace("{name8}", "value8")
                .Replace("{name9}", "value9")
                .Replace("{name10}", "value10"));
    }

    [Fact]
    public void Parse_edge_case1()
    {
        var str = "{name}";
        var replacements = new Dictionary<string, string>
        {
            { "name", "" }
        };
        Formatter.FormatString(str.AsSpan(), replacements).ToString().Should().Be("");
    }

    [Fact]
    public void Parse_edge_case2()
    {
        var str = "{name";
        var replacements = new Dictionary<string, string>
        {
            { "name", "" }
        };
        Formatter.FormatString(str.AsSpan(), replacements).ToString().Should().Be("{name");
    }

    [Fact]
    public void Parse_edge_case3()
    {
        var str = "name}";
        var replacements = new Dictionary<string, string>
        {
            { "name", "" }
        };
        Formatter.FormatString(str.AsSpan(), replacements).ToString().Should().Be("name}");
    }

    [Fact]
    public void Parse_edge_case4()
    {
        var str = "{name}, {name}, {name}";
        var replacements = new Dictionary<string, string>
        {
            { "name", "value" }
        };
        Formatter.FormatString(str.AsSpan(), replacements).ToString().Should().Be("value, value, value");
    }

    [Fact]
    public void Parse_edge_case5()
    {
        var str = "{ {name}, {name}, {name} }";
        var replacements = new Dictionary<string, string>
        {
            { "name", "value" }
        };
        Formatter.FormatString(str.AsSpan(), replacements).ToString().Should().Be("{ value, value, value }");
    }

    private static string GenerateRandomString(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 _-*/*?=()/&%$#\"!";
        var random = new Random(DateTime.Now.Millisecond);

        return new string([.. Enumerable.Repeat(chars, length).Select(s => s[random.Next(s.Length)])]);
    }

    [Fact]
    public void Parse_simple_json_object()
    {
        var json = """{"name": "{name}", "age": {age}}""";
        var replacements = new Dictionary<string, string>
        {
            { "name", "John" },
            { "age", "30" }
        };
        Formatter.FormatString(json.AsSpan(), replacements).ToString()
            .Should().Be("""{"name": "John", "age": 30}""");
    }

    [Fact]
    public void Parse_json_with_nested_object()
    {
        var json = """{"user": {"id": {id}, "email": "{email}"}}""";
        var replacements = new Dictionary<string, string>
        {
            { "id", "123" },
            { "email", "test@example.com" }
        };
        Formatter.FormatString(json.AsSpan(), replacements).ToString()
            .Should().Be("""{"user": {"id": 123, "email": "test@example.com"}}""");
    }

    [Fact]
    public void Parse_json_with_array()
    {
        var json = """{"ids": [{id1}, {id2}, {id3}]}""";
        var replacements = new Dictionary<string, string>
        {
            { "id1", "1" },
            { "id2", "2" },
            { "id3", "3" }
        };
        Formatter.FormatString(json.AsSpan(), replacements).ToString()
            .Should().Be("""{"ids": [1, 2, 3]}""");
    }

    [Fact]
    public void Parse_json_with_array_of_objects()
    {
        var json = """{"users": [{"name": "{name1}"}, {"name": "{name2}"}]}""";
        var replacements = new Dictionary<string, string>
        {
            { "name1", "Alice" },
            { "name2", "Bob" }
        };
        Formatter.FormatString(json.AsSpan(), replacements).ToString()
            .Should().Be("""{"users": [{"name": "Alice"}, {"name": "Bob"}]}""");
    }

    [Fact]
    public void Parse_json_with_boolean_and_null()
    {
        var json = """{"active": {active}, "deleted": {deleted}, "data": {data}}""";
        var replacements = new Dictionary<string, string>
        {
            { "active", "true" },
            { "deleted", "false" },
            { "data", "null" }
        };
        Formatter.FormatString(json.AsSpan(), replacements).ToString()
            .Should().Be("""{"active": true, "deleted": false, "data": null}""");
    }

    [Fact]
    public void Parse_json_with_escaped_quotes_in_value()
    {
        var json = """{"message": "{msg}"}""";
        var replacements = new Dictionary<string, string>
        {
            { "msg", "He said \\\"hello\\\"" }
        };
        Formatter.FormatString(json.AsSpan(), replacements).ToString()
            .Should().Be("{\"message\": \"He said \\\"hello\\\"\"}");
    }

    [Fact]
    public void Parse_json_with_deeply_nested_structure()
    {
        var json = """{"level1": {"level2": {"level3": {"value": "{value}"}}}}""";
        var replacements = new Dictionary<string, string>
        {
            { "value", "deep" }
        };
        Formatter.FormatString(json.AsSpan(), replacements).ToString()
            .Should().Be("""{"level1": {"level2": {"level3": {"value": "deep"}}}}""");
    }

    [Fact]
    public void Parse_json_with_special_characters_in_value()
    {
        var json = """{"query": "{sql}"}""";
        var replacements = new Dictionary<string, string>
        {
            { "sql", "SELECT * FROM users WHERE name = 'John'" }
        };
        Formatter.FormatString(json.AsSpan(), replacements).ToString()
            .Should().Be("""{"query": "SELECT * FROM users WHERE name = 'John'"}""");
    }

    [Fact]
    public void Parse_json_with_unicode_in_value()
    {
        var json = """{"greeting": "{text}"}""";
        var replacements = new Dictionary<string, string>
        {
            { "text", "Привет мир 你好世界" }
        };
        Formatter.FormatString(json.AsSpan(), replacements).ToString()
            .Should().Be("""{"greeting": "Привет мир 你好世界"}""");
    }

    [Fact]
    public void Parse_json_with_newlines_in_value()
    {
        var json = """{"content": "{text}"}""";
        var replacements = new Dictionary<string, string>
        {
            { "text", "line1\nline2\nline3" }
        };
        Formatter.FormatString(json.AsSpan(), replacements).ToString()
            .Should().Be("{\"content\": \"line1\nline2\nline3\"}");
    }

    [Fact]
    public void Parse_json_with_empty_object()
    {
        var json = """{"data": {}}""";
        Formatter.FormatString(json.AsSpan(), []).ToString()
            .Should().Be("""{"data": {}}""");
    }

    [Fact]
    public void Parse_json_with_empty_array()
    {
        var json = """{"items": []}""";
        Formatter.FormatString(json.AsSpan(), []).ToString()
            .Should().Be("""{"items": []}""");
    }

    [Fact]
    public void Parse_json_with_numeric_string_value()
    {
        var json = """{"phone": "{phone}"}""";
        var replacements = new Dictionary<string, string>
        {
            { "phone", "+1-555-123-4567" }
        };
        Formatter.FormatString(json.AsSpan(), replacements).ToString()
            .Should().Be("""{"phone": "+1-555-123-4567"}""");
    }

    [Fact]
    public void Parse_json_with_url_value()
    {
        var json = """{"api_url": "{url}"}""";
        var replacements = new Dictionary<string, string>
        {
            { "url", "https://api.example.com/v1/users?id=123&token=abc" }
        };
        Formatter.FormatString(json.AsSpan(), replacements).ToString()
            .Should().Be("""{"api_url": "https://api.example.com/v1/users?id=123&token=abc"}""");
    }

    [Fact]
    public void Parse_json_with_mixed_types()
    {
        var json = """{"string": "{str}", "number": {num}, "bool": {flag}, "array": [{a}, {b}], "object": {"key": "{val}"}}""";
        var replacements = new Dictionary<string, string>
        {
            { "str", "hello" },
            { "num", "42" },
            { "flag", "true" },
            { "a", "1" },
            { "b", "2" },
            { "val", "nested" }
        };
        Formatter.FormatString(json.AsSpan(), replacements).ToString()
            .Should().Be("""{"string": "hello", "number": 42, "bool": true, "array": [1, 2], "object": {"key": "nested"}}""");
    }

    [Fact]
    public void Parse_json_with_decimal_numbers()
    {
        var json = """{"price": {price}, "tax": {tax}}""";
        var replacements = new Dictionary<string, string>
        {
            { "price", "19.99" },
            { "tax", "0.08" }
        };
        Formatter.FormatString(json.AsSpan(), replacements).ToString()
            .Should().Be("""{"price": 19.99, "tax": 0.08}""");
    }

    [Fact]
    public void Parse_json_with_negative_numbers()
    {
        var json = """{"balance": {balance}, "delta": {delta}}""";
        var replacements = new Dictionary<string, string>
        {
            { "balance", "-100.50" },
            { "delta", "-5" }
        };
        Formatter.FormatString(json.AsSpan(), replacements).ToString()
            .Should().Be("""{"balance": -100.50, "delta": -5}""");
    }

    [Fact]
    public void Parse_json_with_scientific_notation()
    {
        var json = """{"large": {large}, "small": {small}}""";
        var replacements = new Dictionary<string, string>
        {
            { "large", "1.5e10" },
            { "small", "2.3e-5" }
        };
        Formatter.FormatString(json.AsSpan(), replacements).ToString()
            .Should().Be("""{"large": 1.5e10, "small": 2.3e-5}""");
    }

    [Fact]
    public void Parse_json_preserves_whitespace_formatting()
    {
        var json = """
            {
                "name": "{name}",
                "value": {value}
            }
            """;
        var replacements = new Dictionary<string, string>
        {
            { "name", "test" },
            { "value", "123" }
        };
        var result = Formatter.FormatString(json.AsSpan(), replacements).ToString();
        result.Should().Contain("\"name\": \"test\"");
        result.Should().Contain("\"value\": 123");
    }

    [Fact]
    public void Parse_json_with_placeholder_as_entire_value()
    {
        var json = """{value}""";
        var replacements = new Dictionary<string, string>
        {
            { "value", """{"complete": "object"}""" }
        };
        Formatter.FormatString(json.AsSpan(), replacements).ToString()
            .Should().Be("""{"complete": "object"}""");
    }

    [Fact]
    public void Parse_json_with_placeholder_not_found_preserves_original()
    {
        var json = """{"data": "{missing}"}""";
        Formatter.FormatString(json.AsSpan(), []).ToString()
            .Should().Be("""{"data": "{missing}"}""");
    }

    [Fact]
    public void Parse_json_array_at_root()
    {
        var json = """[{id1}, {id2}, {id3}]""";
        var replacements = new Dictionary<string, string>
        {
            { "id1", "1" },
            { "id2", "2" },
            { "id3", "3" }
        };
        Formatter.FormatString(json.AsSpan(), replacements).ToString()
            .Should().Be("""[1, 2, 3]""");
    }

    [Fact]
    public void Parse_json_array_of_strings_at_root()
    {
        var json = """["{a}", "{b}", "{c}"]""";
        var replacements = new Dictionary<string, string>
        {
            { "a", "one" },
            { "b", "two" },
            { "c", "three" }
        };
        Formatter.FormatString(json.AsSpan(), replacements).ToString()
            .Should().Be("""["one", "two", "three"]""");
    }

    [Fact]
    public void Parse_complex_json_api_request()
    {
        var json = """
            {
                "method": "POST",
                "headers": {
                    "Authorization": "Bearer {token}",
                    "Content-Type": "application/json"
                },
                "body": {
                    "user_id": {user_id},
                    "action": "{action}",
                    "timestamp": "{timestamp}",
                    "metadata": {
                        "ip": "{ip}",
                        "user_agent": "{ua}"
                    }
                }
            }
            """;
        var replacements = new Dictionary<string, string>
        {
            { "token", "abc123xyz" },
            { "user_id", "42" },
            { "action", "login" },
            { "timestamp", "2024-01-15T10:30:00Z" },
            { "ip", "192.168.1.1" },
            { "ua", "Mozilla/5.0" }
        };
        var result = Formatter.FormatString(json.AsSpan(), replacements).ToString();
        result.Should().Contain("\"Authorization\": \"Bearer abc123xyz\"");
        result.Should().Contain("\"user_id\": 42");
        result.Should().Contain("\"action\": \"login\"");
        result.Should().Contain("\"timestamp\": \"2024-01-15T10:30:00Z\"");
        result.Should().Contain("\"ip\": \"192.168.1.1\"");
        result.Should().Contain("\"user_agent\": \"Mozilla/5.0\"");
    }
}
