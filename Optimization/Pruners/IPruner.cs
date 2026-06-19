using LlamaServerLauncher.Optimization.Trial;

namespace LlamaServerLauncher.Optimization.Pruners;

public interface IPruner
{
    bool Prune(Study.Study study, FrozenTrial trial);
}
