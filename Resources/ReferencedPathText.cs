using System.Collections.Generic;
using System.Linq;
using LlamaServerLauncher.Models;

namespace LlamaServerLauncher.Resources;

public static class ReferencedPathText
{
    public static string Line(ReferencedPath entry)
    {
        var label = string.IsNullOrEmpty(entry.LabelKey)
            ? entry.Label
            : LocalizedStrings.GetString(entry.LabelKey);

        return $"{label}  {entry.Path}";
    }

    public static string Describe(IReadOnlyList<ReferencedPath> entries)
    {
        if (entries == null || entries.Count == 0)
            return "";

        return LocalizedStrings.Instance.ConfirmMissingFilesMessage
            + "\n" + string.Join("\n", entries.Select(Line));
    }
}
