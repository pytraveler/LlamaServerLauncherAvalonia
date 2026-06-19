using System;

namespace LlamaServerLauncher.Optimization.Distributions;

public sealed class FloatDistribution : Distribution
{
    public double Low { get; }
    public double High { get; }
    public bool Log { get; }
    public double? Step { get; }

    public FloatDistribution(double low, double high, bool log = false, double? step = null)
    {
        if (log && step.HasValue)
            throw new ArgumentException("FloatDistribution: 'log' and 'step' are mutually exclusive.");
        if (log && low <= 0)
            throw new ArgumentException("FloatDistribution: 'low' must be > 0 when 'log' is true.");
        if (step.HasValue && step.Value <= 0)
            throw new ArgumentException("FloatDistribution: 'step' must be positive.");
        if (low > high)
            throw new ArgumentException("FloatDistribution: 'low' must be <= 'high'.");

        Low = low;
        High = high;
        Log = log;
        Step = step;
    }

    public override double ToInternalRepr(object value)
    {
        double v = Convert.ToDouble(value);
        if (double.IsNaN(v))
            throw new ArgumentException("FloatDistribution: value is NaN.");
        return v;
    }

    public override object ToExternalRepr(double internalRepr) => internalRepr;

    public override bool Single()
    {
        if (Step.HasValue)
            return High - Low < Step.Value;
        return Low == High;
    }

    public override bool Contains(object value)
    {
        double v = Convert.ToDouble(value);
        return v >= Low && v <= High;
    }
}
