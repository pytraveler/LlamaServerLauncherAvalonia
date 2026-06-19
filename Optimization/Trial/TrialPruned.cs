using System;

namespace LlamaServerLauncher.Optimization.Trial;

public sealed class TrialPruned : Exception
{
    public TrialPruned() { }
    public TrialPruned(string message) : base(message) { }
}
