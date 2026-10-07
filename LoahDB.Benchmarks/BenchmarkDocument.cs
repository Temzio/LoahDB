using LoahDB;

namespace LoahDB.Benchmarks;

public sealed class BenchmarkDocument : LoahDocument
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public int Age { get; set; }
    public int Score { get; set; }
}
