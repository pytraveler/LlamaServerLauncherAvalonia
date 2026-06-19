using System;
using System.Collections.Generic;
using LlamaServerLauncher.Optimization.Distributions;

namespace LlamaServerLauncher.Optimization.Trial;

public sealed class FrozenTrial
{
    public int TrialId { get; }

    public int Number { get; }

    public TrialState State { get; set; }

    public double? Value { get; set; }

    public Dictionary<string, object> Params { get; }

    public Dictionary<string, double> InternalParams { get; }

    public Dictionary<string, Distribution> Distributions { get; }

    public Dictionary<string, object> UserAttrs { get; }

    public DateTime? DateTimeStart { get; set; }
    public DateTime? DateTimeComplete { get; set; }

    public FrozenTrial(int trialId, int number, TrialState state)
    {
        TrialId = trialId;
        Number = number;
        State = state;
        Params = new Dictionary<string, object>();
        InternalParams = new Dictionary<string, double>();
        Distributions = new Dictionary<string, Distribution>();
        UserAttrs = new Dictionary<string, object>();
    }

    private FrozenTrial(FrozenTrial other)
    {
        TrialId = other.TrialId;
        Number = other.Number;
        State = other.State;
        Value = other.Value;
        Params = new Dictionary<string, object>(other.Params);
        InternalParams = new Dictionary<string, double>(other.InternalParams);
        Distributions = new Dictionary<string, Distribution>(other.Distributions);
        UserAttrs = new Dictionary<string, object>(other.UserAttrs);
        DateTimeStart = other.DateTimeStart;
        DateTimeComplete = other.DateTimeComplete;
    }

    public FrozenTrial Clone() => new(this);
}
