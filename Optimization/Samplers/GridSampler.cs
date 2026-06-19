using System;
using System.Collections.Generic;
using LlamaServerLauncher.Optimization.Distributions;
using LlamaServerLauncher.Optimization.Trial;

namespace LlamaServerLauncher.Optimization.Samplers;

public sealed class GridSampler : ISampler
{
    private readonly List<string> _paramOrder = new();
    private readonly List<Dictionary<string, object>> _grid;

    public GridSampler(IReadOnlyList<KeyValuePair<string, IReadOnlyList<object>>> searchSpace)
    {
        foreach (var kv in searchSpace)
            _paramOrder.Add(kv.Key);
        _grid = BuildCartesianProduct(searchSpace);
    }

    public int GridSize => _grid.Count;

    public double SampleIndependent(Study.Study study, FrozenTrial trial, string paramName, Distribution distribution)
    {
        if (_grid.Count == 0)
            throw new InvalidOperationException("GridSampler: empty grid.");

        var combo = _grid[trial.Number % _grid.Count];
        if (!combo.TryGetValue(paramName, out var value))
            throw new ArgumentException($"GridSampler: parameter '{paramName}' is not part of the grid.");

        return distribution.ToInternalRepr(value);
    }

    private static List<Dictionary<string, object>> BuildCartesianProduct(
        IReadOnlyList<KeyValuePair<string, IReadOnlyList<object>>> searchSpace)
    {
        var combos = new List<Dictionary<string, object>> { new() };
        foreach (var kv in searchSpace)
        {
            var next = new List<Dictionary<string, object>>(combos.Count * Math.Max(1, kv.Value.Count));
            foreach (var partial in combos)
            {
                foreach (var value in kv.Value)
                {
                    var extended = new Dictionary<string, object>(partial) { [kv.Key] = value };
                    next.Add(extended);
                }
            }
            combos = next;
        }
        return combos;
    }
}
