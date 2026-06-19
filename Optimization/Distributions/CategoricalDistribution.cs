using System;
using System.Collections.Generic;
using System.Linq;

namespace LlamaServerLauncher.Optimization.Distributions;

public sealed class CategoricalDistribution : Distribution
{
    public IReadOnlyList<object> Choices { get; }

    public CategoricalDistribution(IReadOnlyList<object> choices)
    {
        if (choices == null || choices.Count == 0)
            throw new ArgumentException("CategoricalDistribution: choices must be non-empty.");
        Choices = choices;
    }

    public override double ToInternalRepr(object value)
    {
        int idx = IndexOf(value);
        if (idx < 0)
            throw new ArgumentException($"CategoricalDistribution: value '{value}' is not among the choices.");
        return idx;
    }

    public override object ToExternalRepr(double internalRepr)
    {
        int idx = (int)Math.Round(internalRepr);
        if (idx < 0 || idx >= Choices.Count)
            throw new ArgumentOutOfRangeException(nameof(internalRepr), $"Index {idx} out of range for {Choices.Count} choices.");
        return Choices[idx];
    }

    public override bool Single() => Choices.Count == 1;

    public override bool Contains(object value) => IndexOf(value) >= 0;

    private int IndexOf(object value)
    {
        for (int i = 0; i < Choices.Count; i++)
        {
            if (Equals(Choices[i], value))
                return i;
        }
        return -1;
    }
}
