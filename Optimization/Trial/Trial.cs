using System;
using System.Collections.Generic;
using LlamaServerLauncher.Optimization.Distributions;

namespace LlamaServerLauncher.Optimization.Trial;

public sealed class Trial
{
    private readonly Study.Study _study;
    private readonly int _trialId;

    public Trial(Study.Study study, int trialId)
    {
        _study = study;
        _trialId = trialId;
        Number = study.Storage.GetTrial(trialId).Number;
    }

    public int TrialId => _trialId;

    public int Number { get; }

    public int SuggestInt(string name, long low, long high, long step = 1, bool log = false)
    {
        var dist = new IntDistribution(low, high, log, step);
        double internalValue = Suggest(name, dist);
        return (int)(long)dist.ToExternalRepr(internalValue);
    }

    public long SuggestLong(string name, long low, long high, long step = 1, bool log = false)
    {
        var dist = new IntDistribution(low, high, log, step);
        double internalValue = Suggest(name, dist);
        return (long)dist.ToExternalRepr(internalValue);
    }

    public double SuggestFloat(string name, double low, double high, double? step = null, bool log = false)
    {
        var dist = new FloatDistribution(low, high, log, step);
        double internalValue = Suggest(name, dist);
        return (double)dist.ToExternalRepr(internalValue);
    }

    public T SuggestCategorical<T>(string name, IReadOnlyList<T> choices)
    {
        var boxed = new object[choices.Count];
        for (int i = 0; i < choices.Count; i++)
            boxed[i] = choices[i]!;
        var dist = new CategoricalDistribution(boxed);
        double internalValue = Suggest(name, dist);
        return (T)dist.ToExternalRepr(internalValue);
    }

    public void SetUserAttr(string key, object value) => _study.Storage.SetTrialUserAttr(_trialId, key, value);

    private double Suggest(string name, Distribution distribution)
    {
        var frozen = _study.Storage.GetTrial(_trialId);
        if (frozen.InternalParams.TryGetValue(name, out var existing))
            return existing;

        double internalValue;
        if (distribution.Single())
            internalValue = SingleInternalValue(distribution);
        else
            internalValue = _study.Sampler.SampleIndependent(_study, frozen, name, distribution);

        _study.Storage.SetTrialParam(_trialId, name, internalValue, distribution);
        return internalValue;
    }

    private static double SingleInternalValue(Distribution distribution) => distribution switch
    {
        FloatDistribution f => f.Low,
        IntDistribution n => n.Low,
        CategoricalDistribution => 0.0,
        _ => throw new NotSupportedException($"No single value for '{distribution.GetType().Name}'.")
    };
}
