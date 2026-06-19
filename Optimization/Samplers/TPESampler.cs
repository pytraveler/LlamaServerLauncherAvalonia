using System;
using System.Collections.Generic;
using System.Linq;
using LlamaServerLauncher.Optimization.Distributions;
using LlamaServerLauncher.Optimization.Study;
using LlamaServerLauncher.Optimization.Trial;

namespace LlamaServerLauncher.Optimization.Samplers;

public sealed class TPESampler : ISampler
{
    private readonly int _nStartupTrials;
    private readonly int _nEiCandidates;
    private readonly Func<int, int> _gamma;
    private readonly ParzenEstimatorParameters _parzenParams;
    private RandomSampler _randomSampler;
    private Random _rng;

    public TPESampler(
        int? seed = null,
        int nStartupTrials = 10,
        int nEiCandidates = 24,
        Func<int, int>? gamma = null,
        ParzenEstimatorParameters? parzenParameters = null)
    {
        _nStartupTrials = nStartupTrials;
        _nEiCandidates = nEiCandidates;
        _gamma = gamma ?? TpeMath.DefaultGamma;
        _parzenParams = parzenParameters ?? new ParzenEstimatorParameters();
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
        _randomSampler = new RandomSampler(seed);
    }

    public void Reseed(int seed)
    {
        _rng = new Random(seed);
        _randomSampler = new RandomSampler(seed);
    }

    public double SampleIndependent(Study.Study study, FrozenTrial trial, string paramName, Distribution distribution)
    {
        var trials = study.GetTrials(new[] { TrialState.Complete, TrialState.Pruned })
            .Where(t => t.Value.HasValue && t.Params.ContainsKey(paramName))
            .ToList();

        if (trials.Count < _nStartupTrials)
            return _randomSampler.SampleIndependent(study, trial, paramName, distribution);

        int n = trials.Count;
        int nBelow = Math.Min(_gamma(n), n);

        List<FrozenTrial> sorted = study.Direction == StudyDirection.Maximize
            ? trials.OrderByDescending(t => t.Value!.Value).ToList()
            : trials.OrderBy(t => t.Value!.Value).ToList();

        double[] below = ObservationsOf(sorted.Take(nBelow), paramName, distribution);
        double[] above = ObservationsOf(sorted.Skip(nBelow), paramName, distribution);

        var mpeBelow = new ParzenEstimator(below, distribution, _parzenParams);
        var mpeAbove = new ParzenEstimator(above, distribution, _parzenParams);

        double[] samples = mpeBelow.Sample(_rng, _nEiCandidates);
        double[] logBelow = mpeBelow.LogPdf(samples);
        double[] logAbove = mpeAbove.LogPdf(samples);

        int bestIdx = 0;
        double bestAcq = double.NegativeInfinity;
        for (int i = 0; i < samples.Length; i++)
        {
            double acq = logBelow[i] - logAbove[i];
            if (acq > bestAcq)
            {
                bestAcq = acq;
                bestIdx = i;
            }
        }

        return samples[bestIdx];
    }

    private static double[] ObservationsOf(IEnumerable<FrozenTrial> trials, string paramName, Distribution distribution)
    {
        var values = new List<double>();
        foreach (var t in trials)
        {
            if (t.InternalParams.TryGetValue(paramName, out var internalValue))
                values.Add(internalValue);
            else if (t.Params.TryGetValue(paramName, out var external))
                values.Add(distribution.ToInternalRepr(external));
        }
        return values.ToArray();
    }
}
