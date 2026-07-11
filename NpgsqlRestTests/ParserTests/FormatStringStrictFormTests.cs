namespace NpgsqlRestTests.ParserTests;

/// <summary>
/// The strict placeholder forms in <see cref="Formatter.FormatString(ReadOnlySpan{char}, Dictionary{string, string})"/>:
/// <list type="bullet">
/// <item><c>{!name}</c> - resolves the <c>"!name"</c> entry (registered by the client only for names that
/// actually resolved to a real value), falling back to the plain <c>"name"</c> entry.</item>
/// <item><c>{!name:fallback}</c> - resolves the <c>"!name"</c> entry; when only the plain entry exists
/// (the name is known but carried no resolved value) the inline fallback text is substituted instead.</item>
/// <item>Unknown names are left verbatim in every form, so arbitrary brace content
/// (JSON, CSS, JS, Serilog format specifiers) is never consumed.</item>
/// </list>
/// </summary>
public class FormatStringStrictFormTests
{
    private static string Format(string input, Dictionary<string, string> replacements) =>
        Formatter.FormatString(input.AsSpan(), replacements).ToString();

    [Fact]
    public void Bang_form_resolves_the_bang_key()
    {
        var replacements = new Dictionary<string, string> { ["HOST"] = "db1", ["!HOST"] = "db1" };
        Format("h={!HOST};", replacements).Should().Be("h=db1;");
    }

    [Fact]
    public void Bang_form_without_bang_key_aliases_the_plain_entry()
    {
        // A dictionary populated without the "!name" convention (e.g. set directly by a library user):
        // {!name} still resolves like {name}.
        var replacements = new Dictionary<string, string> { ["HOST"] = "db1" };
        Format("h={!HOST};", replacements).Should().Be("h=db1;");
    }

    [Fact]
    public void Fallback_form_uses_the_value_when_the_bang_key_exists()
    {
        var replacements = new Dictionary<string, string> { ["HOST"] = "db1", ["!HOST"] = "db1" };
        Format("h={!HOST:localhost};", replacements).Should().Be("h=db1;");
    }

    [Fact]
    public void Fallback_form_uses_the_fallback_when_only_the_plain_key_exists()
    {
        // Plain key without bang key = the name is known (allowlisted) but carried no resolved value.
        var replacements = new Dictionary<string, string> { ["HOST"] = "" };
        Format("h={!HOST:localhost};", replacements).Should().Be("h=localhost;");
    }

    [Fact]
    public void Fallback_form_with_unknown_name_is_left_verbatim()
    {
        var replacements = new Dictionary<string, string> { ["other"] = "x" };
        Format("h={!HOST:localhost};", replacements).Should().Be("h={!HOST:localhost};");
    }

    [Fact]
    public void Bang_form_with_unknown_name_is_left_verbatim()
    {
        var replacements = new Dictionary<string, string> { ["other"] = "x" };
        Format("h={!HOST};", replacements).Should().Be("h={!HOST};");
    }

    [Fact]
    public void Fallback_may_contain_colons()
    {
        var replacements = new Dictionary<string, string> { ["URL"] = "" };
        Format("u={!URL:http://localhost:5000};", replacements).Should().Be("u=http://localhost:5000;");
    }

    [Fact]
    public void Empty_fallback_resolves_to_the_empty_string()
    {
        var replacements = new Dictionary<string, string> { ["TAG"] = "" };
        Format("t=[{!TAG:}]", replacements).Should().Be("t=[]");
    }

    [Fact]
    public void Empty_name_is_left_verbatim()
    {
        var replacements = new Dictionary<string, string> { ["x"] = "1" };
        Format("v={!:fallback};", replacements).Should().Be("v={!:fallback};");
        Format("v={!};", replacements).Should().Be("v={!};");
    }

    [Fact]
    public void Plain_colon_tokens_are_never_treated_as_fallbacks()
    {
        // Brace-colon content that only LOOKS like a fallback form must survive verbatim:
        // Serilog format specifiers, inline CSS, JS/TS object literals.
        var replacements = new Dictionary<string, string> { ["Message"] = "hi", ["border"] = "b" };
        Format("[{Timestamp:HH:mm:ss} {Message}]", replacements).Should().Be("[{Timestamp:HH:mm:ss} hi]");
        Format("td{border:1px solid red}", replacements).Should().Be("td{border:1px solid red}");
        Format("{status: number; title: string}", replacements).Should().Be("{status: number; title: string}");
    }

    [Fact]
    public void Css_important_is_left_verbatim()
    {
        var replacements = new Dictionary<string, string> { ["color"] = "x", ["!color"] = "x" };
        // token starts with 'c', not '!' - stays a direct-lookup miss and is preserved
        // (a non-empty dictionary so the early passthrough shortcut does not apply)
        Format("h1{color:red !important}", replacements).Should().Be("h1{color:red !important}");
        // and even with a matching name, the colon+space content resolves per the strict rule
        Format("v={!color:red};", replacements).Should().Be("v=x;");
    }
}
