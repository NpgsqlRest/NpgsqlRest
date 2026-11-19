using System.Text;

namespace NpgsqlRestTests.ParserTests;

/// <summary>
/// The UTF-8 converters (used to render rows as bytes, without a string per value) must produce exactly what the
/// string-based converters produce, once that is encoded as UTF-8.
/// </summary>
public class PgUtf8ConvertersTests
{
    private static string Utf8(Action<Utf8Buffer> write)
    {
        var buffer = Utf8Buffer.Rent();
        try
        {
            write(buffer);
            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }
        finally
        {
            Utf8Buffer.Return(buffer);
        }
    }

    private static string JsonStringUtf8(string value) =>
        Utf8(b => PgUtf8Converters.AppendJsonString(b, Encoding.UTF8.GetBytes(value)));

    [Fact]
    public void JsonString_matches_SerializeString_for_every_BMP_character()
    {
        var mismatches = new List<string>();
        for (int c = 0; c <= 0xFFFF; c++)
        {
            if (char.IsSurrogate((char)c))
            {
                continue; // not encodable on their own; pairs are covered below
            }
            // on its own, and in the middle of text (so the "first character to escape" offset is not 0)
            foreach (var text in new[] { ((char)c).ToString(), "ab" + (char)c + "cd" })
            {
                var expected = PgConverters.SerializeString(text);
                var actual = JsonStringUtf8(text);
                if (expected != actual && mismatches.Count < 10)
                {
                    mismatches.Add($"U+{c:X4}: expected {expected} actual {actual}");
                }
            }
        }
        mismatches.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain ascii")]
    [InlineData("quote \" backslash \\ slash / tab \t newline \n return \r")]
    [InlineData("žćčđš ünïcödé — “quotes” €")]
    [InlineData("emoji 😀 and 🇭🇷 and 𝄞")]
    [InlineData("<script>alert('x')</script> & more")]
    [InlineData("\u0000\u0001\u001f\u007f\u0080\u009f\u00a0\u2028\u2029\ufeff\ufffd")]
    [InlineData("{\"json\": [1, 2, {\"nested\": \"value\"}]}")]
    public void JsonString_matches_SerializeString(string text)
    {
        JsonStringUtf8(text).Should().Be(PgConverters.SerializeString(text));
    }

    [Fact]
    public void JsonString_matches_SerializeString_for_long_and_random_text()
    {
        var random = new Random(12345);
        var alphabet = "abc XYZ 019 \"\\\n\t\u0001žš€😀\u00a0\u2028".EnumerateRunes().ToArray();
        for (int n = 0; n < 500; n++)
        {
            var sb = new StringBuilder();
            int length = random.Next(0, 300);
            for (int i = 0; i < length; i++)
            {
                sb.Append(alphabet[random.Next(alphabet.Length)].ToString());
            }
            var text = sb.ToString();
            JsonStringUtf8(text).Should().Be(PgConverters.SerializeString(text));
        }
        var big = new string('x', 100_000) + "\"" + new string('y', 100_000);
        JsonStringUtf8(big).Should().Be(PgConverters.SerializeString(big));
    }

    public static IEnumerable<object[]> ArrayCases()
    {
        string[] types = ["text[]", "varchar[]", "integer[]", "numeric[]", "boolean[]", "timestamp[]", "timestamptz[]", "date[]", "json[]", "jsonb[]", "uuid[]"];
        string[] inputs =
        [
            "{}",
            "{a}",
            "{a,b,c}",
            "{1,2,3}",
            "{1.5,-2,NULL}",
            "{t,f,NULL}",
            "{NULL}",
            "{\"NULL\",NULL,null}",
            "{\"a b\",\"c,d\",\"e\\\"f\",\"g\\\\h\"}",
            "{\"a\\\\\",\"b,c\",\"d\\\"e\"}",
            "{\"\",\"x\"}",
            "{{1,2},{3,4}}",
            "{{a,b},{c,NULL}}",
            "{{{1},{2}},{{3},{4}}}",
            "{\"2026-01-01 10:00:00\",\"2026-01-02 11:30:00+02\"}",
            "{2026-01-01,2026-12-31}",
            "{\"{\\\"a\\\": 1}\",\"{\\\"b\\\": [1, 2]}\"}",
            "{žćč,\"ünï cödé\",😀}",
            "{\"line\nbreak\",\"tab\tchar\"}",
            "{a0eebc99-9c0b-4ef8-bb6d-6bb9bd380a11,NULL}",
            "not an array",
            "",
            "{",
            "{a",
        ];
        foreach (var type in types)
        {
            foreach (var input in inputs)
            {
                yield return [type, input];
            }
        }
    }

    [Theory]
    [MemberData(nameof(ArrayCases))]
    public void Array_matches_PgArrayToJsonArray(string type, string input)
    {
        var descriptor = new TypeDescriptor(type);
        var expected = PgConverters.PgArrayToJsonArray(input, descriptor).ToString();
        var actual = Utf8(b => PgUtf8Converters.AppendPgArrayAsJson(b, Encoding.UTF8.GetBytes(input), descriptor));
        actual.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain")]
    [InlineData("with \"quotes\" inside")]
    [InlineData("\"\"")]
    [InlineData("žćč \"😀\"")]
    public void CsvQuoted_matches_QuoteText(string text)
    {
        Utf8(b => PgUtf8Converters.AppendCsvQuoted(b, Encoding.UTF8.GetBytes(text))).Should().Be(PgConverters.QuoteText(text));
    }

    [Theory]
    [InlineData("2026-01-01 10:00:00+00")]
    [InlineData("2026-01-01")]
    [InlineData("10:00:00")]
    [InlineData("infinity")]
    [InlineData("")]
    public void QuotedDateTime_matches_QuoteDateTime(string text)
    {
        ReadOnlySpan<char> span = text;
        var expected = PgConverters.QuoteDateTime(ref span);
        Utf8(b => PgUtf8Converters.AppendQuotedDateTime(b, Encoding.UTF8.GetBytes(text))).Should().Be(expected);
    }

    [Fact]
    public void Utf8Buffer_appends_and_grows()
    {
        var buffer = Utf8Buffer.Rent();
        try
        {
            buffer.Append('{');
            buffer.Append("\"ključ\":");
            buffer.Append("vrijednost 😀".AsSpan());
            buffer.Append((byte)',');
            buffer.Append(new string('z', 5000)); // forces growth
            buffer.Append('é');
            buffer.ToString().Should().Be("{\"ključ\":vrijednost 😀," + new string('z', 5000) + "é");
            buffer.Clear();
            buffer.Length.Should().Be(0);
        }
        finally
        {
            Utf8Buffer.Return(buffer);
        }
    }

    [Fact]
    public void Utf8Buffer_is_reused_from_the_pool()
    {
        var first = Utf8Buffer.Rent();
        first.Append("used");
        Utf8Buffer.Return(first);
        var second = Utf8Buffer.Rent();
        second.Should().BeSameAs(first);
        second.Length.Should().Be(0);
        Utf8Buffer.Return(second);
    }
}
