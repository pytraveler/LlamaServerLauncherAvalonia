using System.Threading;
using System.Threading.Tasks;
using LlamaServerLauncher.Models.Optimization;

namespace LlamaServerLauncher.Services.Optimization;

public interface IConfigBenchmarker
{
    Task<BenchmarkResult> RunBenchAsync(BenchArgs args, int timeoutSeconds, CancellationToken ct);

    string Describe(BenchArgs args);
}
