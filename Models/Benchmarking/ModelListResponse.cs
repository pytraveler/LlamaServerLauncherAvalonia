using System;
using System.Collections.Generic;
using System.Text.Json;

namespace LlamaServerLauncher.Models.Benchmarking;

public static class ModelListResponse
{
    public static List<string> Parse(string body)
    {
        var ids = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return ids;
            foreach (var item in data.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;
                if (item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                {
                    var text = id.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                        ids.Add(text!);
                }
            }
        }
        catch
        {
            // not a model list
        }
        return ids;
    }

    public static string? Choose(IReadOnlyList<string> ids, string? preferred)
    {
        if (ids == null || ids.Count == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(preferred))
        {
            var want = preferred!.Trim();
            foreach (var id in ids)
            {
                if (string.Equals(id, want, StringComparison.OrdinalIgnoreCase))
                    return id;
            }
            foreach (var id in ids)
            {
                if (id.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0
                    || want.IndexOf(id, StringComparison.OrdinalIgnoreCase) >= 0)
                    return id;
            }
        }

        return ids[0];
    }
}
