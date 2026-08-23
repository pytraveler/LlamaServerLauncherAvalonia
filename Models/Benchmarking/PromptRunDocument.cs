using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace LlamaServerLauncher.Models.Benchmarking;

public static class PromptRunDocument
{
    public static List<string> SplitPrompts(string? text)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return result;

        var current = new StringBuilder();
        foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (IsSeparator(raw))
            {
                Flush(result, current);
                continue;
            }
            current.Append(raw).Append('\n');
        }
        Flush(result, current);
        return result;
    }

    public static bool IsSeparator(string line)
    {
        var trimmed = (line ?? string.Empty).Trim();
        if (trimmed.Length < 3)
            return false;
        foreach (var c in trimmed)
        {
            if (c != '-')
                return false;
        }
        return true;
    }

    private static void Flush(List<string> result, StringBuilder current)
    {
        var text = current.ToString().Trim();
        current.Clear();
        if (text.Length > 0)
            result.Add(text);
    }

    public static string BuildMarkdown(BenchmarkRun run, PromptRunReport report, Func<string, string>? localize = null)
    {
        string L(string s) => localize?.Invoke(s) ?? s;

        var sb = new StringBuilder();
        sb.Append("# ").Append(L("Prompt run")).Append(": ").AppendLine(run.DisplayName);
        sb.AppendLine();

        sb.Append("- ").Append(L("Profile")).Append(": ").AppendLine(run.ProfileName);
        sb.Append("- ").Append(L("Model")).Append(": ").AppendLine(ModelName(run));
        sb.Append("- ").Append(L("Date")).Append(": ")
          .AppendLine(run.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        sb.Append("- ").Append(L("Mode")).Append(": ")
          .AppendLine(report.KeepContext ? L("Conversation") : L("Independent requests"));
        sb.Append("- ").Append(L("Max tokens")).Append(": ")
          .AppendLine(report.MaxTokens > 0 ? report.MaxTokens.ToString(CultureInfo.InvariantCulture) : L("Server default"));
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(report.SystemPrompt))
        {
            sb.Append("## ").AppendLine(L("System prompt"));
            sb.AppendLine();
            AppendFenced(sb, report.SystemPrompt);
            sb.AppendLine();
        }

        foreach (var turn in report.Turns)
        {
            sb.Append("## ").Append(L("Request")).Append(' ').Append(turn.Index).AppendLine();
            sb.AppendLine();
            AppendFenced(sb, turn.Prompt);
            sb.AppendLine();

            if (turn.Failed)
            {
                sb.Append("**").Append(L("Failed")).Append("**: ").AppendLine(OneLine(turn.Error));
                sb.AppendLine();
                continue;
            }

            if (!string.IsNullOrWhiteSpace(turn.Reasoning))
            {
                sb.Append("### ").AppendLine(L("Reasoning"));
                sb.AppendLine();
                AppendQuoted(sb, turn.Reasoning!);
                sb.AppendLine();
            }

            sb.Append("### ").AppendLine(L("Answer"));
            sb.AppendLine();
            sb.AppendLine(turn.Response.TrimEnd());
            sb.AppendLine();
            sb.Append('_').Append(TurnStats(turn, L)).AppendLine("_");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public static string BuildSummarySection(PromptRunReport report, Func<string, string>? localize = null)
    {
        string L(string s) => localize?.Invoke(s) ?? s;

        var sb = new StringBuilder();
        sb.Append("## ").AppendLine(L("Prompt run"));
        sb.AppendLine();

        if (report.Turns.Count == 0)
        {
            sb.Append('_').Append(L("No requests were sent.")).AppendLine("_");
            return sb.ToString();
        }

        sb.Append("| ").Append(L("Request")).Append(" | ").Append(L("Prompt tok/s"))
          .Append(" | ").Append(L("Gen tok/s")).Append(" | ").Append(L("TTFT, ms"))
          .Append(" | ").Append(L("Tokens")).Append(" | ").Append(L("Duration, s")).AppendLine(" |");
        sb.AppendLine("| --- | --- | --- | --- | --- | --- |");

        foreach (var turn in report.Turns)
        {
            sb.Append("| ").Append(turn.Index).Append(" | ");
            if (turn.Failed)
            {
                sb.Append(L("Failed")).Append(" | | | | ")
                  .Append(Num(turn.DurationSeconds, 1)).AppendLine(" |");
                continue;
            }
            sb.Append(Num(turn.PromptTps)).Append(" | ")
              .Append(Num(turn.GenTps)).Append(" | ")
              .Append(Num(turn.TtftMs, 0)).Append(" | ")
              .Append(turn.PredictedTokens?.ToString(CultureInfo.InvariantCulture) ?? "-").Append(" | ")
              .Append(Num(turn.DurationSeconds, 1)).AppendLine(" |");
        }

        sb.AppendLine();
        sb.Append('_').Append(L("Full transcript: prompt-run.md")).AppendLine("_");
        return sb.ToString();
    }

    private static string TurnStats(PromptRunTurn turn, Func<string, string> L)
    {
        var parts = new List<string>();
        if (turn.PromptTokens.HasValue || turn.PromptTps.HasValue)
            parts.Add($"{L("Prompt")}: {turn.PromptTokens?.ToString(CultureInfo.InvariantCulture) ?? "-"} tok, {Num(turn.PromptTps)} tok/s");
        if (turn.PredictedTokens.HasValue || turn.GenTps.HasValue)
            parts.Add($"{L("Generation")}: {turn.PredictedTokens?.ToString(CultureInfo.InvariantCulture) ?? "-"} tok, {Num(turn.GenTps)} tok/s");
        if (turn.TtftMs.HasValue)
            parts.Add($"{L("TTFT, ms")}: {Num(turn.TtftMs, 0)}");
        parts.Add($"{L("Duration, s")}: {Num(turn.DurationSeconds, 1)}");
        if (!string.IsNullOrWhiteSpace(turn.FinishReason))
            parts.Add($"{L("Finish reason")}: {turn.FinishReason}");
        return string.Join("; ", parts);
    }

    private static string ModelName(BenchmarkRun run)
    {
        var path = run.ConfigSnapshot?.ModelPath;
        if (!string.IsNullOrWhiteSpace(path))
            return Path.GetFileName(path);
        if (!string.IsNullOrWhiteSpace(run.ConfigSnapshot?.HfFile))
            return run.ConfigSnapshot!.HfFile;
        if (!string.IsNullOrWhiteSpace(run.ConfigSnapshot?.Alias))
            return run.ConfigSnapshot!.Alias;
        return "-";
    }

    private static void AppendFenced(StringBuilder sb, string text)
    {
        var fence = new string('`', Math.Max(3, LongestBacktickRun(text) + 1));
        sb.AppendLine(fence);
        sb.AppendLine(text.TrimEnd());
        sb.AppendLine(fence);
    }

    private static void AppendQuoted(StringBuilder sb, string text)
    {
        foreach (var line in text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd().Split('\n'))
            sb.Append("> ").AppendLine(line);
    }

    private static int LongestBacktickRun(string text)
    {
        int best = 0, run = 0;
        foreach (var c in text ?? string.Empty)
        {
            if (c == '`')
            {
                run++;
                if (run > best) best = run;
            }
            else
            {
                run = 0;
            }
        }
        return best;
    }

    private static string OneLine(string? text) =>
        (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();

    private static string Num(double? value, int decimals = 2)
    {
        if (!value.HasValue)
            return "-";
        return value.Value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }
}
