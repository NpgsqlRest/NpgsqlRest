using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using NpgsqlRest;

namespace BenchmarkTests;

[MemoryDiagnoser]
public class SimdBenchmarks
{
    private string _smallTemplate = null!;
    private string _largeTemplate = null!;
    private Dictionary<string, string> _replacements = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Template inputs
        _smallTemplate = "Hello, {name}! Welcome to {place}.";
        _largeTemplate = string.Join(" ", Enumerable.Range(1, 100).Select(i => $"{{key{i}}}"));

        // Replacements
        _replacements = new Dictionary<string, string>
        {
            { "name", "John" },
            { "place", "NpgsqlRest" }
        };
        for (int i = 1; i <= 100; i++)
        {
            _replacements[$"key{i}"] = $"value{i}";
        }
    }

    #region FormatString Benchmarks

    [Benchmark]
    public ReadOnlySpan<char> FormatString_Small()
    {
        return Formatter.FormatString(_smallTemplate.AsSpan(), _replacements);
    }

    [Benchmark]
    public ReadOnlySpan<char> FormatString_Large()
    {
        return Formatter.FormatString(_largeTemplate.AsSpan(), _replacements);
    }

    #endregion

    #region IsPatternMatch Benchmarks

    [Benchmark]
    public bool PatternMatch_NoWildcard()
    {
        return Parser.IsPatternMatch("verylongfilename.txt", "verylongfilename.txt");
    }

    [Benchmark]
    public bool PatternMatch_ExtensionMatch()
    {
        return Parser.IsPatternMatch("verylongfilename.txt", "*.txt");
    }

    [Benchmark]
    public bool PatternMatch_WildcardMiddle()
    {
        return Parser.IsPatternMatch("prefix_middle_suffix", "prefix*suffix");
    }

    [Benchmark]
    public bool PatternMatch_LongLiteralPrefix()
    {
        return Parser.IsPatternMatch("this_is_a_very_long_filename_that_does_not_match.txt", "this_is_a_very_long_filename_that_matches*");
    }

    #endregion
}
