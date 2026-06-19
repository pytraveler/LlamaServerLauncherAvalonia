using System;
using LlamaServerLauncher.Optimization.Distributions;
using LlamaServerLauncher.Optimization.Trial;

namespace LlamaServerLauncher.Optimization.Samplers;

public sealed class RandomSampler : ISampler
{
    private Random _rng;

    public RandomSampler(int? seed = null)
    {
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    public void Reseed(int seed) => _rng = new Random(seed);

    public double SampleIndependent(Study.Study study, FrozenTrial trial, string paramName, Distribution distribution)
        => Sample(_rng, distribution);

    public static double Sample(Random rng, Distribution distribution)
    {
        switch (distribution)
        {
            case FloatDistribution f:
            {
                if (f.Single())
                    return f.Low;
                if (f.Log)
                {
                    double logLow = Math.Log(f.Low);
                    double logHigh = Math.Log(f.High);
                    return Math.Exp(logLow + rng.NextDouble() * (logHigh - logLow));
                }
                if (f.Step is { } step)
                {
                    long k = (long)Math.Floor((f.High - f.Low) / step + 1e-9);
                    long pick = (long)(rng.NextDouble() * (k + 1));
                    if (pick > k) pick = k;
                    return f.Low + pick * step;
                }
                return f.Low + rng.NextDouble() * (f.High - f.Low);
            }
            case IntDistribution n:
            {
                if (n.Single())
                    return n.Low;
                if (n.Log)
                {
                    double logLow = Math.Log(n.Low);
                    double logHigh = Math.Log(n.High + 1.0);
                    double sampled = Math.Exp(logLow + rng.NextDouble() * (logHigh - logLow));
                    long v = (long)Math.Floor(sampled);
                    if (v < n.Low) v = n.Low;
                    if (v > n.High) v = n.High;
                    return v;
                }
                else
                {
                    long k = (n.High - n.Low) / n.Step;
                    long pick = (long)(rng.NextDouble() * (k + 1));
                    if (pick > k) pick = k;
                    return n.Low + pick * n.Step;
                }
            }
            case CategoricalDistribution c:
                return rng.Next(c.Choices.Count);
            default:
                throw new NotSupportedException($"RandomSampler cannot sample '{distribution.GetType().Name}'.");
        }
    }
}
