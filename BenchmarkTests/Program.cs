using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using BenchmarkTests;
using Perfolizer.Horology;

// Parse command-line arguments
var runHttpBenchmarks = args.Contains("--http") || args.Contains("-h");
var runInProcessBenchmarks = args.Contains("--inprocess") || args.Contains("-i");
var runAll = args.Contains("--all") || args.Contains("-a");

// Default: run in-process benchmarks if no arguments provided
if (!runHttpBenchmarks && !runInProcessBenchmarks && !runAll)
{
    runInProcessBenchmarks = true;
}

// In-process benchmarks (no external server required)
if (runInProcessBenchmarks || runAll)
{
    Console.WriteLine("=== Running In-Process Benchmarks ===");
    Console.WriteLine("These benchmarks test NpgsqlRest internals without requiring a running server.\n");

    BenchmarkRunner.Run<TypeCategoryBenchmarks>();
    BenchmarkRunner.Run<ParameterParserBenchmarks>();
    BenchmarkRunner.Run<SerializationBenchmarks>();
}

// HTTP benchmarks (require external server)
if (runHttpBenchmarks || runAll)
{
    Console.WriteLine("\n=== Running HTTP Endpoint Benchmarks ===");
    Console.WriteLine("IMPORTANT: These benchmarks require the NpgsqlRestTests server to be running.");
    Console.WriteLine("Start the server with: dotnet run --project NpgsqlRestTests/Setup\n");

    Console.WriteLine("Press ENTER to continue (or Ctrl+C to cancel)...");
    Console.ReadLine();

    var httpConfig = DefaultConfig.Instance
        .AddJob(Job.Default
            .WithToolchain(new InProcessEmitToolchain(timeout: TimeSpan.FromSeconds(30), logOutput: false))
            .WithLaunchCount(1)
            .WithWarmupCount(3)
            .WithIterationCount(20)
            .WithIterationTime(TimeInterval.FromMilliseconds(500)))
        .AddLogger(new ConsoleLogger(unicodeSupport: true, ConsoleLogger.CreateGrayScheme()))
        .WithOptions(ConfigOptions.DisableLogFile);

    // Run HTTP benchmarks
    BenchmarkRunner.Run<HttpClientTests>(httpConfig);
    BenchmarkRunner.Run<HttpScalingBenchmarks>(httpConfig);
    BenchmarkRunner.Run<HttpTypeSerializationBenchmarks>(httpConfig);
    // Concurrency benchmarks can cause connection issues, run separately if needed
    // BenchmarkRunner.Run<HttpConcurrencyBenchmarks>(httpConfig);
}

Console.WriteLine("\n=== Benchmark Complete ===");
Console.WriteLine("Results saved to BenchmarkDotNet.Artifacts/ folder.");

// Usage help
if (args.Contains("--help") || args.Contains("-?"))
{
    Console.WriteLine(@"
NpgsqlRest Benchmark Tests

Usage: dotnet run -- [options]

Options:
  -i, --inprocess   Run in-process benchmarks only (default)
                    Tests TypeCategory, ParameterParser, Serialization internals
                    No external server required

  -h, --http        Run HTTP endpoint benchmarks only
                    Requires NpgsqlRestTests server running on localhost:5000
                    Start with: dotnet run --project NpgsqlRestTests/Setup

  -a, --all         Run all benchmarks (in-process + HTTP)

  -?, --help        Show this help message

Examples:
  dotnet run                     # Run in-process benchmarks
  dotnet run -- --http           # Run HTTP benchmarks
  dotnet run -- --all            # Run all benchmarks
");
}
