using System;

namespace LlamaServerLauncher.Optimization.Distributions;

public sealed class IntDistribution : Distribution
{
    public long Low { get; }
    public long High { get; }
    public bool Log { get; }
    public long Step { get; }

    public IntDistribution(long low, long high, bool log = false, long step = 1)
    {
        if (step <= 0)
            throw new ArgumentException("IntDistribution: 'step' must be positive.");
        if (log && step != 1)
            throw new ArgumentException("IntDistribution: 'step' must be 1 when 'log' is true.");
        if (log && low < 1)
            throw new ArgumentException("IntDistribution: 'low' must be >= 1 when 'log' is true.");
        if (low > high)
            throw new ArgumentException("IntDistribution: 'low' must be <= 'high'.");

        Low = low;
        High = high;
        Log = log;
        Step = step;
    }

    public override double ToInternalRepr(object value) => Convert.ToInt64(value);

    public override object ToExternalRepr(double internalRepr) => (long)Math.Round(internalRepr);

    public override bool Single()
    {
        return (High - Low) < Step;
    }

    public override bool Contains(object value)
    {
        long v = Convert.ToInt64(value);
        return v >= Low && v <= High && (v - Low) % Step == 0;
    }
}
