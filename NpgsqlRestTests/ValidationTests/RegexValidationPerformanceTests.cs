using System.Diagnostics;

namespace NpgsqlRestTests.ValidationTests;

/// <summary>
/// Security regression: Regex validation rules run on request values in linear time (or are cut off by a match
/// timeout). The built-in email rule used to take quadratic time on crafted input: "a@" followed by many dots and a
/// space took ~0.7 s at 40 KB and grew 4x for every doubling.
/// </summary>
public class RegexValidationPerformanceTests
{
    private static readonly ValidationRule Email = new ValidationOptions().Rules["email"];

    [Fact]
    public void Email_rule_still_matches_as_before()
    {
        Email.GetRegex()!.IsMatch("user@example.com").Should().BeTrue();
        Email.GetRegex()!.IsMatch("not-an-email").Should().BeFalse();
        Email.GetRegex()!.IsMatch("a b@example.com").Should().BeFalse();
        Email.GetRegex()!.IsMatch("user@localhost").Should().BeFalse();
    }

    [Fact]
    public void Email_rule_is_linear_on_crafted_input()
    {
        var input = "a@" + new string('.', 400_000) + " ";
        var sw = Stopwatch.StartNew();
        Email.GetRegex()!.IsMatch(input).Should().BeFalse();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1)); // backtracking: over a minute
    }

    [Fact]
    public void Pattern_the_linear_engine_cannot_run_falls_back_with_a_timeout()
    {
        var rule = new ValidationRule { Type = ValidationType.Regex, Pattern = @"^(a+)+\1$" }; // backreference
        var regex = rule.GetRegex()!;
        regex.Options.Should().NotHaveFlag(System.Text.RegularExpressions.RegexOptions.NonBacktracking);
        regex.MatchTimeout.Should().Be(ValidationRule.RegexMatchTimeout);
        regex.IsMatch("aaaa").Should().BeTrue();
    }

    [Fact]
    public void Changing_the_pattern_rebuilds_the_regex()
    {
        var rule = new ValidationRule { Type = ValidationType.Regex, Pattern = "^a$" };
        rule.GetRegex()!.IsMatch("a").Should().BeTrue();
        rule.Pattern = "^b$";
        rule.GetRegex()!.IsMatch("a").Should().BeFalse();
        rule.GetRegex()!.IsMatch("b").Should().BeTrue();
    }
}
