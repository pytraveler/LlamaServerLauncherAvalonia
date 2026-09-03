using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LlamaServerLauncher.Models;

public sealed class ImportedCommand
{
    public string? ExecutablePath { get; init; }

    public IReadOnlyList<string> Tokens { get; init; } = Array.Empty<string>();

    public bool HasCommand => Tokens.Count > 0;

    public static readonly ImportedCommand None = new();
}

public static class CommandImport
{
    private const string ExeName = "llama-server";

    private static readonly char[] Separators = { '/', '\\' };

    private static readonly char[] Continuations = { '\\', '^', '`' };

    private static readonly HashSet<string> ModelArguments = new(StringComparer.OrdinalIgnoreCase)
    {
        "-m", "--model", "-mu", "--model-url",
        "--models-dir", "--hf-repo", "-hf", "-hfr", "--hf-repo-draft",
        "-hfd", "-hfrd", "--hf-file", "-hff", "--hf-repo-v", "-hfv",
        "--hf-file-v", "--docker-repo", "-dr"
    };

    private static readonly HashSet<string> Terminators = new(StringComparer.Ordinal)
    {
        "|", "||", "&", "&&", ";", ">", ">>", "1>", "2>", "2>&1", "&>", "|&"
    };

    public static ImportedCommand FromText(string? text)
    {
        foreach (var line in LogicalLines(text))
        {
            var command = FromLine(line);
            if (command.HasCommand) return command;
        }

        return ImportedCommand.None;
    }

    public static IReadOnlyList<string> LogicalLines(string? text)
    {
        var lines = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return lines;

        var pending = new StringBuilder();

        foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = raw.Trim();

            if (line.Length == 0 || IsComment(line))
            {
                if (line.Length == 0) Flush(lines, pending);
                continue;
            }

            line = StripPrompt(line);

            bool continued = EndsWithContinuation(line);
            if (continued) line = line[..^1].TrimEnd();

            if (pending.Length > 0) pending.Append(' ');
            pending.Append(line);

            if (!continued) Flush(lines, pending);
        }

        Flush(lines, pending);
        return lines;
    }

    public static bool IsLlamaServer(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;

        var name = token.Trim().Trim('"', '\'');
        int cut = name.LastIndexOfAny(Separators);
        if (cut >= 0) name = name[(cut + 1)..];

        return name.Equals(ExeName, StringComparison.OrdinalIgnoreCase)
            || name.Equals(ExeName + ".exe", StringComparison.OrdinalIgnoreCase);
    }

    public static bool HasModelArgument(IReadOnlyList<string> tokens)
    {
        for (int i = 0; i < tokens.Count; i++)
        {
            if (!ModelArguments.Contains(tokens[i])) continue;
            if (tokens[i].Equals("--models-dir", StringComparison.OrdinalIgnoreCase)) return true;
            if (i + 1 < tokens.Count && !tokens[i + 1].StartsWith('-')) return true;
        }

        return false;
    }

    private static ImportedCommand FromLine(string line)
    {
        var tokens = CommandLineParser.ParseArguments(line);
        if (tokens.Count == 0) return ImportedCommand.None;

        int exeIndex = -1;
        for (int i = 0; i < tokens.Count; i++)
        {
            if (!IsLlamaServer(tokens[i])) continue;
            exeIndex = i;
            break;
        }

        var arguments = Truncate(exeIndex >= 0 ? tokens.Skip(exeIndex + 1) : tokens);
        if (!HasModelArgument(arguments)) return ImportedCommand.None;

        return new ImportedCommand
        {
            ExecutablePath = exeIndex >= 0 ? ExecutableOf(tokens[exeIndex]) : null,
            Tokens = arguments,
        };
    }

    private static List<string> Truncate(IEnumerable<string> tokens)
    {
        var kept = new List<string>();
        foreach (var token in tokens)
        {
            if (Terminators.Contains(token)) break;
            if (token.Length > 1 && (token[0] == '>' || token.StartsWith("2>", StringComparison.Ordinal))) break;
            kept.Add(token);
        }

        while (kept.Count > 0)
        {
            var last = kept[^1].TrimEnd();
            if (last is "\\" or "^" or "`" or ";" || last.EndsWith(';')) kept.RemoveAt(kept.Count - 1);
            else break;
        }

        return kept;
    }

    private static string? ExecutableOf(string token)
    {
        var path = token.Trim().Trim('"', '\'');
        if (path.Length == 0) return null;

        try
        {
            return Path.IsPathRooted(path) ? path : null;
        }
        catch
        {
            return null;
        }
    }

    private static void Flush(List<string> lines, StringBuilder pending)
    {
        if (pending.Length == 0) return;
        lines.Add(pending.ToString());
        pending.Clear();
    }

    private static bool IsComment(string line) =>
        line.StartsWith('#')
        || line.StartsWith("//", StringComparison.Ordinal)
        || line.StartsWith("::", StringComparison.Ordinal)
        || line.StartsWith("```", StringComparison.Ordinal)
        || line.StartsWith("rem ", StringComparison.OrdinalIgnoreCase);

    private static string StripPrompt(string line)
    {
        if (line.StartsWith("PS ", StringComparison.OrdinalIgnoreCase))
        {
            int end = line.IndexOf('>');
            if (end > 0 && end + 1 < line.Length) return line[(end + 1)..].TrimStart();
        }

        if (line.Length > 1 && (line[0] == '$' || line[0] == '>') && char.IsWhiteSpace(line[1]))
            return line[2..].TrimStart();

        return line;
    }

    private static bool EndsWithContinuation(string line)
    {
        if (line.Length < 2) return false;
        char last = line[^1];
        if (Array.IndexOf(Continuations, last) < 0) return false;
        return char.IsWhiteSpace(line[^2]);
    }
}
