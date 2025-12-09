using NpgsqlRest.HttpClientType;

namespace NpgsqlRestTests;

[Collection("TestFixture")]
public class ParseHttpTypeDefinitionTests
{
    private readonly HttpClientTypes _parser = new(null, null);

    [Fact]
    public void Returns_null_for_null_input()
    {
        var result = _parser.ParseHttpTypeDefinition(null!);
        result.Should().BeNull();
    }

    [Fact]
    public void Returns_null_for_empty_string()
    {
        var result = _parser.ParseHttpTypeDefinition("");
        result.Should().BeNull();
    }

    [Fact]
    public void Returns_null_for_whitespace_only()
    {
        var result = _parser.ParseHttpTypeDefinition("   \t\n  ");
        result.Should().BeNull();
    }

    [Fact]
    public void Returns_null_for_invalid_method()
    {
        var result = _parser.ParseHttpTypeDefinition("INVALID https://example.com");
        result.Should().BeNull();
    }

    [Fact]
    public void Returns_null_for_missing_url()
    {
        var result = _parser.ParseHttpTypeDefinition("GET");
        result.Should().BeNull();
    }

    [Fact]
    public void Returns_null_for_method_only_with_space()
    {
        var result = _parser.ParseHttpTypeDefinition("GET ");
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("GET https://example.com", "GET", "https://example.com")]
    [InlineData("POST https://api.example.com/users", "POST", "https://api.example.com/users")]
    [InlineData("PUT https://api.example.com/users/123", "PUT", "https://api.example.com/users/123")]
    [InlineData("PATCH https://api.example.com/users/123", "PATCH", "https://api.example.com/users/123")]
    [InlineData("DELETE https://api.example.com/users/123", "DELETE", "https://api.example.com/users/123")]
    public void Parses_simple_request_line(string input, string expectedMethod, string expectedUrl)
    {
        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Method.Should().Be(expectedMethod);
        result.Url.Should().Be(expectedUrl);
        result.Headers.Should().BeNull();
        result.Body.Should().BeNull();
        result.ContentType.Should().BeNull();
        result.Timeout.Should().BeNull();
    }

    [Theory]
    [InlineData("get https://example.com", "GET")]
    [InlineData("Get https://example.com", "GET")]
    [InlineData("post https://example.com", "POST")]
    [InlineData("Post https://example.com", "POST")]
    [InlineData("pOsT https://example.com", "POST")]
    public void Handles_case_insensitive_methods(string input, string expectedMethod)
    {
        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Method.Should().Be(expectedMethod);
    }

    [Theory]
    [InlineData("GET https://example.com HTTP/1.1", "https://example.com")]
    [InlineData("POST https://api.example.com/data HTTP/2", "https://api.example.com/data")]
    [InlineData("PUT https://example.com/path HTTP/1.0", "https://example.com/path")]
    public void Ignores_http_version_suffix(string input, string expectedUrl)
    {
        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Url.Should().Be(expectedUrl);
    }

    [Fact]
    public void Parses_single_header()
    {
        var input = """
            GET https://example.com
            Authorization: Bearer token123
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Headers.Should().NotBeNull();
        result.Headers.Should().ContainKey("Authorization");
        result.Headers!["Authorization"].Should().Be("Bearer token123");
    }

    [Fact]
    public void Parses_multiple_headers()
    {
        var input = """
            POST https://api.example.com/data
            Authorization: Bearer token123
            X-Custom-Header: custom-value
            Accept: application/json
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Headers.Should().NotBeNull();
        result.Headers.Should().HaveCount(3);
        result.Headers!["Authorization"].Should().Be("Bearer token123");
        result.Headers["X-Custom-Header"].Should().Be("custom-value");
        result.Headers["Accept"].Should().Be("application/json");
    }

    [Fact]
    public void Extracts_content_type_to_dedicated_property()
    {
        var input = """
            POST https://api.example.com/data
            Content-Type: application/json
            Authorization: Bearer token
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.ContentType.Should().Be("application/json");
        result.Headers.Should().NotBeNull();
        result.Headers.Should().NotContainKey("Content-Type");
        result.Headers!["Authorization"].Should().Be("Bearer token");
    }

    [Fact]
    public void Content_type_is_case_insensitive()
    {
        var input = """
            POST https://api.example.com/data
            content-type: text/plain
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.ContentType.Should().Be("text/plain");
        result.Headers.Should().BeNull();
    }

    [Fact]
    public void Parses_body_after_empty_line()
    {
        var input = """
            POST https://api.example.com/data
            Content-Type: application/json

            {"key": "value"}
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Body.Should().Be("{\"key\": \"value\"}");
    }

    [Fact]
    public void Parses_multiline_body()
    {
        var input = """
            POST https://api.example.com/data
            Content-Type: application/json

            {
                "name": "John",
                "age": 30
            }
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Body.Should().Contain("\"name\": \"John\"");
        result.Body.Should().Contain("\"age\": 30");
    }

    // Timeout format: "timeout <value>" (space separator)
    [Fact]
    public void Parses_timeout_with_space_separator()
    {
        var input = """
            timeout 00:00:30
            GET https://api.example.com/slow
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Timeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    // Timeout format: "timeout=<value>" (equals separator)
    [Fact]
    public void Parses_timeout_with_equals_separator()
    {
        var input = """
            timeout=00:00:30
            GET https://api.example.com/slow
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Timeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    // Timeout format: "timeout: <value>" (colon separator)
    [Fact]
    public void Parses_timeout_with_colon_separator()
    {
        var input = """
            timeout: 00:00:30
            GET https://api.example.com/slow
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Timeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    // Timeout format: "@timeout <value>" (with @ prefix, space separator)
    [Fact]
    public void Parses_at_timeout_with_space_separator()
    {
        var input = """
            @timeout 00:00:30
            GET https://api.example.com/slow
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Timeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    // Timeout format: "@timeout=<value>" (with @ prefix, equals separator)
    [Fact]
    public void Parses_at_timeout_with_equals_separator()
    {
        var input = """
            @timeout=00:00:30
            GET https://api.example.com/slow
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Timeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    // Timeout format: "@timeout: <value>" (with @ prefix, colon separator)
    [Fact]
    public void Parses_at_timeout_with_colon_separator()
    {
        var input = """
            @timeout: 00:00:30
            GET https://api.example.com/slow
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Timeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    // Timeout format: "# timeout <value>" (with # prefix)
    [Fact]
    public void Parses_hash_timeout_with_space_separator()
    {
        var input = """
            # timeout 00:00:30
            GET https://api.example.com/slow
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Timeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    // Timeout format: "# @timeout <value>" (with # and @ prefix)
    [Fact]
    public void Parses_hash_at_timeout_with_space_separator()
    {
        var input = """
            # @timeout 00:00:30
            GET https://api.example.com/slow
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Timeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    // Timeout value: digits only (treated as seconds)
    [Fact]
    public void Parses_timeout_digits_only_as_seconds()
    {
        var input = """
            timeout 30
            GET https://api.example.com/slow
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Timeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    // Timeout value: TimeSpan format with colon
    [Fact]
    public void Parses_timeout_timespan_format()
    {
        var input = """
            timeout 00:01:30
            GET https://api.example.com/slow
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Timeout.Should().Be(TimeSpan.FromSeconds(90));
    }

    // Timeout value: TimeSpan format hours:minutes:seconds
    [Fact]
    public void Parses_timeout_timespan_format_hours()
    {
        var input = """
            timeout 01:30:00
            GET https://api.example.com/slow
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Timeout.Should().Be(TimeSpan.FromMinutes(90));
    }

    // Timeout value: PostgresInterval format (seconds)
    [Fact]
    public void Parses_timeout_postgres_interval_seconds()
    {
        var input = """
            timeout 45s
            GET https://api.example.com/slow
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Timeout.Should().Be(TimeSpan.FromSeconds(45));
    }

    // Timeout value: PostgresInterval format (minutes)
    [Fact]
    public void Parses_timeout_postgres_interval_minutes()
    {
        var input = """
            timeout 5m
            GET https://api.example.com/slow
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Timeout.Should().Be(TimeSpan.FromMinutes(5));
    }

    // Timeout value: PostgresInterval format (hours)
    [Fact]
    public void Parses_timeout_postgres_interval_hours()
    {
        var input = """
            timeout 1h
            GET https://api.example.com/very-slow
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Timeout.Should().Be(TimeSpan.FromHours(1));
    }

    // Timeout value: PostgresInterval format (milliseconds)
    [Fact]
    public void Parses_timeout_postgres_interval_milliseconds()
    {
        var input = """
            timeout 500ms
            GET https://api.example.com/fast
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Timeout.Should().Be(TimeSpan.FromMilliseconds(500));
    }

    // Timeout: case insensitive keyword
    [Fact]
    public void Parses_timeout_case_insensitive()
    {
        var input = """
            TIMEOUT 30
            GET https://api.example.com/slow
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Timeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    // Timeout: invalid value defaults to 30 seconds
    [Fact]
    public void Parses_timeout_invalid_value_defaults_to_30_seconds()
    {
        var input = """
            timeout invalid_value
            GET https://api.example.com/slow
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Timeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    // Timeout: decimal seconds with PostgresInterval
    [Fact]
    public void Parses_timeout_decimal_postgres_interval()
    {
        var input = """
            timeout 1.5s
            GET https://api.example.com/data
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Timeout.Should().Be(TimeSpan.FromSeconds(1.5));
    }

    [Fact]
    public void Ignores_comment_lines_that_are_not_directives()
    {
        var input = """
            # This is a comment
            # Another comment
            GET https://api.example.com/data
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Method.Should().Be("GET");
        result.Url.Should().Be("https://api.example.com/data");
    }

    [Fact]
    public void Parses_complete_request_with_all_parts()
    {
        var input = """
            timeout 00:00:30
            POST https://api.example.com/users HTTP/1.1
            Content-Type: application/json
            Authorization: Bearer token123
            X-Request-Id: abc-123

            {"name": "John", "email": "john@example.com"}
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Method.Should().Be("POST");
        result.Url.Should().Be("https://api.example.com/users");
        result.Timeout.Should().Be(TimeSpan.FromSeconds(30));
        result.ContentType.Should().Be("application/json");
        result.Headers.Should().HaveCount(2);
        result.Headers!["Authorization"].Should().Be("Bearer token123");
        result.Headers["X-Request-Id"].Should().Be("abc-123");
        result.Body.Should().Be("{\"name\": \"John\", \"email\": \"john@example.com\"}");
    }

    [Fact]
    public void Handles_url_with_query_parameters()
    {
        var input = "GET https://api.example.com/search?q=test&page=1&limit=10";

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Url.Should().Be("https://api.example.com/search?q=test&page=1&limit=10");
    }

    [Fact]
    public void Handles_url_with_port()
    {
        var input = "GET http://localhost:8080/api/data";

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Url.Should().Be("http://localhost:8080/api/data");
    }

    [Fact]
    public void Handles_header_with_colon_in_value()
    {
        var input = """
            GET https://api.example.com
            X-Timestamp: 2024:01:15:10:30:00
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Headers.Should().NotBeNull();
        result.Headers!["X-Timestamp"].Should().Be("2024:01:15:10:30:00");
    }

    [Fact]
    public void Handles_crlf_line_endings()
    {
        var input = "timeout 30\r\nGET https://example.com\r\nAuthorization: Bearer token\r\n\r\n{\"test\": true}";

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Method.Should().Be("GET");
        result.Timeout.Should().Be(TimeSpan.FromSeconds(30));
        result.Headers!["Authorization"].Should().Be("Bearer token");
        result.Body.Should().Be("{\"test\": true}");
    }

    [Fact]
    public void Handles_lf_line_endings()
    {
        var input = "timeout 30\nGET https://example.com\nAuthorization: Bearer token\n\n{\"test\": true}";

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Method.Should().Be("GET");
        result.Timeout.Should().Be(TimeSpan.FromSeconds(30));
        result.Headers!["Authorization"].Should().Be("Bearer token");
        result.Body.Should().Be("{\"test\": true}");
    }

    [Fact]
    public void Handles_mixed_line_endings()
    {
        var input = "timeout 30\r\nGET https://example.com\nAuthorization: Bearer token\r\n\n{\"test\": true}";

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Method.Should().Be("GET");
        result.Url.Should().Be("https://example.com");
    }

    [Fact]
    public void Handles_empty_body_after_headers()
    {
        var input = """
            GET https://example.com
            Authorization: Bearer token

            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Body.Should().BeNull();
    }

    [Fact]
    public void Handles_whitespace_only_body()
    {
        var input = "GET https://example.com\nAuthorization: Bearer token\n\n   \t  \n  ";

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Body.Should().BeNull();
    }

    [Fact]
    public void Handles_no_headers_with_body()
    {
        var input = """
            POST https://api.example.com/data

            raw body content
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Headers.Should().BeNull();
        result.Body.Should().Be("raw body content");
    }

    [Fact]
    public void Trims_header_names_and_values()
    {
        var input = """
            GET https://example.com
              Authorization  :   Bearer token
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Headers!["Authorization"].Should().Be("Bearer token");
    }

    [Fact]
    public void Handles_url_with_placeholders()
    {
        var input = "GET https://api.example.com/users/{{user_id}}/posts/{{post_id}}";

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Url.Should().Be("https://api.example.com/users/{{user_id}}/posts/{{post_id}}");
    }

    [Fact]
    public void Handles_body_with_placeholders()
    {
        var input = """
            POST https://api.example.com/data
            Content-Type: application/json

            {"id": {{id}}, "name": "{{name}}"}
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Body.Should().Be("{\"id\": {{id}}, \"name\": \"{{name}}\"}");
    }

    [Fact]
    public void Handles_header_with_placeholder()
    {
        var input = """
            GET https://api.example.com/data
            Authorization: Bearer {{token}}
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Headers!["Authorization"].Should().Be("Bearer {{token}}");
    }

    [Fact]
    public void Handles_multiple_directives_before_request()
    {
        var input = """
            # This is a comment
            timeout 60s
            # Another comment
            GET https://api.example.com/data
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Timeout.Should().Be(TimeSpan.FromSeconds(60));
        result.Method.Should().Be("GET");
    }

    [Fact]
    public void Last_timeout_directive_wins()
    {
        var input = """
            timeout 30s
            timeout 60s
            GET https://api.example.com/data
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Timeout.Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void Returns_null_for_only_comments()
    {
        var input = """
            # This is a comment
            # Another comment
            timeout 30s
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().BeNull();
    }

    [Fact]
    public void Handles_xml_body()
    {
        var input = """
            POST https://api.example.com/xml
            Content-Type: application/xml

            <?xml version="1.0"?>
            <root>
                <item>value</item>
            </root>
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.ContentType.Should().Be("application/xml");
        result.Body.Should().Contain("<root>");
        result.Body.Should().Contain("<item>value</item>");
    }

    [Fact]
    public void Handles_form_urlencoded_body()
    {
        var input = """
            POST https://api.example.com/form
            Content-Type: application/x-www-form-urlencoded

            name=John&email=john@example.com&age=30
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.ContentType.Should().Be("application/x-www-form-urlencoded");
        result.Body.Should().Be("name=John&email=john@example.com&age=30");
    }

    [Fact]
    public void Handles_request_with_only_method_and_url()
    {
        var result = _parser.ParseHttpTypeDefinition("DELETE https://api.example.com/resource/123");

        result.Should().NotBeNull();
        result!.Method.Should().Be("DELETE");
        result.Url.Should().Be("https://api.example.com/resource/123");
        result.Headers.Should().BeNull();
        result.Body.Should().BeNull();
        result.ContentType.Should().BeNull();
        result.Timeout.Should().BeNull();
    }

    [Fact]
    public void Headers_dictionary_is_case_insensitive()
    {
        var input = """
            GET https://example.com
            X-Custom-Header: value1
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Headers!["x-custom-header"].Should().Be("value1");
        result.Headers["X-CUSTOM-HEADER"].Should().Be("value1");
        result.Headers["X-Custom-Header"].Should().Be("value1");
    }

    [Theory]
    [InlineData("HEAD https://example.com")]
    [InlineData("OPTIONS https://example.com")]
    [InlineData("TRACE https://example.com")]
    [InlineData("CONNECT https://example.com")]
    public void Returns_null_for_unsupported_http_methods(string input)
    {
        var result = _parser.ParseHttpTypeDefinition(input);
        result.Should().BeNull();
    }

    [Fact]
    public void Handles_empty_lines_before_request()
    {
        var input = """


            GET https://example.com
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Method.Should().Be("GET");
    }

    [Fact]
    public void Timeout_with_full_word_units()
    {
        var input = """
            timeout 30 seconds
            GET https://example.com
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Timeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Timeout_with_minute_word()
    {
        var input = """
            timeout 2 minutes
            GET https://example.com
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.Timeout.Should().Be(TimeSpan.FromMinutes(2));
    }

    // NeedsParsing tests

    [Fact]
    public void NeedsParsing_is_false_when_no_placeholders()
    {
        var input = """
            GET https://api.example.com/users
            Authorization: Bearer token123
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.NeedsParsing.Should().BeFalse();
    }

    [Fact]
    public void NeedsParsing_is_true_when_url_has_placeholder()
    {
        var input = "GET https://api.example.com/users/{id}";

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.NeedsParsing.Should().BeTrue();
    }

    [Fact]
    public void NeedsParsing_is_true_when_url_has_multiple_placeholders()
    {
        var input = "GET https://api.example.com/{version}/users/{id}";

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.NeedsParsing.Should().BeTrue();
    }

    [Fact]
    public void NeedsParsing_is_true_when_header_has_placeholder()
    {
        var input = """
            GET https://api.example.com/data
            Authorization: Bearer {token}
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.NeedsParsing.Should().BeTrue();
    }

    [Fact]
    public void NeedsParsing_is_true_when_content_type_has_placeholder()
    {
        var input = """
            POST https://api.example.com/data
            Content-Type: {contentType}
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.NeedsParsing.Should().BeTrue();
    }

    [Fact]
    public void NeedsParsing_is_true_when_body_has_placeholder()
    {
        var input = """
            POST https://api.example.com/users
            Content-Type: application/json

            {"name": "{name}", "email": "{email}"}
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.NeedsParsing.Should().BeTrue();
    }

    [Fact]
    public void NeedsParsing_is_true_when_multiple_parts_have_placeholders()
    {
        var input = """
            POST https://api.example.com/{version}/users/{id}
            Authorization: Bearer {token}
            Content-Type: application/json

            {"action": "{action}"}
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.NeedsParsing.Should().BeTrue();
    }

    [Fact]
    public void NeedsParsing_is_false_for_empty_braces()
    {
        var input = "GET https://api.example.com/{}";

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.NeedsParsing.Should().BeFalse();
    }

    [Fact]
    public void NeedsParsing_is_false_for_json_without_placeholders()
    {
        var input = """
            POST https://api.example.com/data
            Content-Type: application/json

            {"name": "John", "age": 30}
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.NeedsParsing.Should().BeFalse();
    }

    [Fact]
    public void NeedsParsing_detects_placeholder_in_query_string()
    {
        var input = "GET https://api.example.com/search?q={query}&limit={limit}";

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.NeedsParsing.Should().BeTrue();
    }

    [Fact]
    public void NeedsParsing_is_true_for_single_char_placeholder()
    {
        var input = "GET https://api.example.com/users/{x}";

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.NeedsParsing.Should().BeTrue();
    }

    [Fact]
    public void NeedsParsing_handles_nested_json_with_placeholder()
    {
        var input = """
            POST https://api.example.com/data
            Content-Type: application/json

            {"user": {"id": {userId}, "settings": {"theme": "{theme}"}}}
            """;

        var result = _parser.ParseHttpTypeDefinition(input);

        result.Should().NotBeNull();
        result!.NeedsParsing.Should().BeTrue();
    }
}
