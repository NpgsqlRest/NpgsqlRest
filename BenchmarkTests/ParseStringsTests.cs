using BenchmarkDotNet.Attributes;
using NpgsqlRest;

namespace BenchmarkTests;

public class ParseStringsTests
{
    readonly string[] names = ["test.txt", "x", "verylongfilename.txt", "abcde"];
    readonly string[] patterns = ["*.txt", "*?*", "*long*.txt", "a?c*"];

    [Benchmark]
    public void IsPatternMatchMethod()
    {
        foreach (var name in names)
            foreach (var pattern in patterns)
            {
                Parser.IsPatternMatch(name, pattern);
            }
    }

    //[Benchmark]
    //public void LikePatternIsMethod()
    //{
    //    foreach (var name in names)
    //        foreach (var pattern in patterns)
    //        {
    //            DefaultResponseParser.LikePatternIsMatch(name, pattern);
    //        }
    //}

    //[Benchmark]
    //public void LikePatternIsMatchFastMethod()
    //{
    //    foreach (var name in names)
    //        foreach (var pattern in patterns)
    //        {
    //            DefaultResponseParser.LikePatternIsMatchFast(name, pattern);
    //        }
    //}
}
