namespace LlamaServerLauncher.Optimization.Trial;

public enum TrialState
{
    Running = 0,
    Complete = 1,
    Pruned = 2,
    Fail = 3,
    Waiting = 4,
}

public static class TrialStateExtensions
{
    public static bool IsFinished(this TrialState state) =>
        state != TrialState.Running && state != TrialState.Waiting;
}
