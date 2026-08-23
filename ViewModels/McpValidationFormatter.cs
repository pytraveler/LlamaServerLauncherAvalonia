using System.Collections.Generic;
using LlamaServerLauncher.Models;
using LlamaServerLauncher.Resources;

namespace LlamaServerLauncher.ViewModels;

internal static class McpValidationFormatter
{
    public static string Format(IEnumerable<McpConfigIssue> issues)
    {
        var loc = LocalizedStrings.Instance;
        var lines = new List<string>();

        foreach (var issue in issues)
        {
            var text = issue.Kind switch
            {
                McpIssueKind.EmptyName => loc.McpIssueEmptyName,
                McpIssueKind.EmptyCommand => string.Format(loc.McpIssueEmptyCommand, issue.ServerName),
                McpIssueKind.DuplicateName => string.Format(loc.McpIssueDuplicateName, issue.ServerName),
                McpIssueKind.InvalidTimeout => string.Format(loc.McpIssueInvalidTimeout, issue.ServerName),
                _ => string.Empty
            };

            if (text.Length > 0 && !lines.Contains(text))
                lines.Add(text);
        }

        return string.Join("\n", lines);
    }
}
