using BenchmarkDotNet.Running;
using LoahDB.Benchmarks;

if (args.Length >= 2 && args[0] == "--quick-report")
{
    QuickBenchmarkReport.WriteReport(Path.GetFullPath(args[1]));
    return;
}

BenchmarkRunner.Run<DocumentStoreBenchmarks>();
