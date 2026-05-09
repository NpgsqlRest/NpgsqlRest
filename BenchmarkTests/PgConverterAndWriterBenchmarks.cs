using System.Buffers;
using System.IO.Pipelines;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using NpgsqlRest;

namespace BenchmarkTests;

// Focused micro-benchmarks for the optimization changes.
// Run with: dotnet run -c Release --project BenchmarkTests -- --filter "*PgConverterAndWriterBenchmarks*"
//
// Each benchmark targets one of:
//   - Step 2: writer.Write(ROSpan<byte>) vs CopyTo+GetSpan+Advance dance (in-run comparison; no stash needed)
//   - Step 3: PgArrayToJsonArray / PgCompositeArrayToJsonArray / PgTupleToJsonObject (run on each branch to A/B)

[ShortRunJob]
[MemoryDiagnoser]
public class PgConverterAndWriterBenchmarks
{
    // Realistic-shaped inputs
    private static readonly string NumericArray100 =
        "{" + string.Join(",", Enumerable.Range(0, 100).Select(i => i.ToString())) + "}";
    private static readonly string TextArray100 =
        "{" + string.Join(",", Enumerable.Range(0, 100).Select(i => $"\"item_{i}_value\"")) + "}";
    private static readonly string CompositeArray50 =
        "{" + string.Join(",", Enumerable.Range(0, 50).Select(i => $"\"({i},\\\"name_{i}\\\",true)\"")) + "}";
    private static readonly string Tuple10 = "(1,\"name\",true,2.5,\"more text\",42,\"another\",false,1.5,\"end\")";

    private static readonly TypeDescriptor NumericArrayDesc = new("integer[]");
    private static readonly TypeDescriptor TextArrayDesc = new("text[]");
    private static readonly string[] CompositeFieldNames = ["id", "name", "active"];
    private static readonly TypeDescriptor[] CompositeFieldDescs =
        [new("integer"), new("text"), new("boolean")];
    private static readonly string[] Tuple10Fields =
        ["a", "b", "c", "d", "e", "f", "g", "h", "i", "j"];
    private static readonly TypeDescriptor[] Tuple10Descs =
    [
        new("integer"), new("text"), new("boolean"), new("numeric"),
        new("text"), new("integer"), new("text"), new("boolean"),
        new("numeric"), new("text"),
    ];

    // ----- Step 3: PgConverters (run on each branch via git stash to A/B) -----

    [Benchmark]
    public int PgArray_Numeric_100()
    {
        var span = NumericArray100.AsSpan();
        var result = PgConverters.PgArrayToJsonArray(span, NumericArrayDesc);
        return result.Length;
    }

    [Benchmark]
    public int PgArray_Text_100()
    {
        var span = TextArray100.AsSpan();
        var result = PgConverters.PgArrayToJsonArray(span, TextArrayDesc);
        return result.Length;
    }

    [Benchmark]
    public int PgCompositeArray_50()
    {
        var span = CompositeArray50.AsSpan();
        var result = PgConverters.PgCompositeArrayToJsonArray(span, CompositeFieldNames, CompositeFieldDescs);
        return result.Length;
    }

    [Benchmark]
    public int PgTuple_10fields()
    {
        var span = Tuple10.AsSpan();
        var result = PgConverters.PgTupleToJsonObject(span, Tuple10Fields, Tuple10Descs);
        return result.Length;
    }

    // ----- Step 2: writer.Write vs CopyTo+GetSpan+Advance pattern (in-run A/B) -----
    // These two compile on both branches because Consts.Utf8X is implicit-convertible to ROSpan<byte>
    // either way (byte[] -> ROSpan<byte> implicit, or already ROSpan<byte>).

    [Benchmark]
    public int Writer_CopyToPattern_500()
    {
        var bw = new ArrayBufferWriter<byte>(2048);
        for (int i = 0; i < 500; i++)
        {
            Consts.Utf8OpenBrace.CopyTo(bw.GetSpan(1)); bw.Advance(1);
            Consts.Utf8Comma.CopyTo(bw.GetSpan(1)); bw.Advance(1);
            Consts.Utf8CloseBrace.CopyTo(bw.GetSpan(1)); bw.Advance(1);
            Consts.Utf8Null.CopyTo(bw.GetSpan(4)); bw.Advance(4);
        }
        return bw.WrittenCount;
    }

    [Benchmark]
    public int Writer_DirectPattern_500()
    {
        var bw = new ArrayBufferWriter<byte>(2048);
        for (int i = 0; i < 500; i++)
        {
            bw.Write(Consts.Utf8OpenBrace);
            bw.Write(Consts.Utf8Comma);
            bw.Write(Consts.Utf8CloseBrace);
            bw.Write(Consts.Utf8Null);
        }
        return bw.WrittenCount;
    }
}
