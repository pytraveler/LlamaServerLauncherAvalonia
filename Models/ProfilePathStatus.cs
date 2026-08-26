using System;
using System.Collections.Generic;

namespace LlamaServerLauncher.Models;

public static class ProfilePathStatus
{
    private static readonly object Gate = new();
    private static Dictionary<string, IReadOnlyList<ReferencedPath>> _missing =
        new(StringComparer.OrdinalIgnoreCase);

    public static bool Update(Dictionary<string, IReadOnlyList<ReferencedPath>> missing)
    {
        missing ??= new Dictionary<string, IReadOnlyList<ReferencedPath>>(StringComparer.OrdinalIgnoreCase);

        lock (Gate)
        {
            if (SameAsCurrent(missing))
                return false;

            _missing = new Dictionary<string, IReadOnlyList<ReferencedPath>>(missing, StringComparer.OrdinalIgnoreCase);
            return true;
        }
    }

    public static bool IsBroken(string? profileName)
    {
        if (string.IsNullOrEmpty(profileName))
            return false;

        lock (Gate)
            return _missing.ContainsKey(profileName);
    }

    public static IReadOnlyList<ReferencedPath>? MissingFor(string? profileName)
    {
        if (string.IsNullOrEmpty(profileName))
            return null;

        lock (Gate)
            return _missing.TryGetValue(profileName, out var entries) ? entries : null;
    }

    public static void Clear()
    {
        lock (Gate)
            _missing = new Dictionary<string, IReadOnlyList<ReferencedPath>>(StringComparer.OrdinalIgnoreCase);
    }

    private static bool SameAsCurrent(Dictionary<string, IReadOnlyList<ReferencedPath>> candidate)
    {
        if (candidate.Count != _missing.Count)
            return false;

        foreach (var kvp in candidate)
        {
            if (!_missing.TryGetValue(kvp.Key, out var existing) || !SameEntries(existing, kvp.Value))
                return false;
        }

        return true;
    }

    private static bool SameEntries(IReadOnlyList<ReferencedPath> a, IReadOnlyList<ReferencedPath> b)
    {
        if (a.Count != b.Count)
            return false;

        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].Path != b[i].Path || a[i].LabelKey != b[i].LabelKey || a[i].Label != b[i].Label)
                return false;
        }

        return true;
    }
}
