using LlamaServerLauncher.Optimization.Trial;

namespace LlamaServerLauncher.Optimization.Pruners;

public sealed class NopPruner : IPruner
{
    public bool Prune(Study.Study study, FrozenTrial trial) => false;
}
