using System;
using System.Linq;
using LlamaServerLauncher.Models.Benchmarking;
using LlamaServerLauncher.Services.Benchmarking;

public static class PromptRunTests
{
    public static void Run(Harness h)
    {
        h.Section("PromptRunDocument.SplitPrompts");

        var none = PromptRunDocument.SplitPrompts("   \n  \n");
        h.Check("blank text gives no requests", none.Count == 0, none.Count.ToString());

        var single = PromptRunDocument.SplitPrompts("First line\n\nStill the same request\nand its tail\n");
        h.Check("no separator means one request", single.Count == 1, single.Count.ToString());
        h.Check("blank lines survive inside a request",
            single.Count == 1 && single[0].Contains("\n\nStill the same request"), single.FirstOrDefault() ?? "");

        var many = PromptRunDocument.SplitPrompts("one\r\n---\r\ntwo\r\n-----\r\nthree\r\n");
        h.Check("separators split requests", many.Count == 3, string.Join(" | ", many));
        h.Check("longer dash runs also separate", many.Count == 3 && many[2] == "three", many.LastOrDefault() ?? "");

        var edges = PromptRunDocument.SplitPrompts("---\n\nonly one\n---\n   \n---\n");
        h.Check("empty segments are dropped", edges.Count == 1 && edges[0] == "only one", string.Join(" | ", edges));

        h.Check("two dashes are not a separator", !PromptRunDocument.IsSeparator("--"), "ok");
        h.Check("dashed text is not a separator", !PromptRunDocument.IsSeparator("- item"), "ok");
        h.Check("padded dashes are a separator", PromptRunDocument.IsSeparator("  ---  "), "ok");

        h.Section("PromptRunReport aggregates");

        var report = new PromptRunReport { SystemPrompt = "You are terse.", KeepContext = true, MaxTokens = 256 };
        report.Turns.Add(new PromptRunTurn { Index = 1, Prompt = "a", Response = "A", GenTps = 40, PromptTps = 1000, TtftMs = 100, PredictedTokens = 10, PromptTokens = 5, DurationSeconds = 1.5 });
        report.Turns.Add(new PromptRunTurn { Index = 2, Prompt = "b", Response = "B", GenTps = 60, PromptTps = 2000, TtftMs = 200, PredictedTokens = 20, PromptTokens = 7, DurationSeconds = 2.5 });
        report.Turns.Add(new PromptRunTurn { Index = 3, Prompt = "c", Error = "boom", DurationSeconds = 0.5 });

        h.Check("completed and failed counted", report.CompletedTurns == 2 && report.FailedTurns == 1,
            $"{report.CompletedTurns}/{report.FailedTurns}");
        h.Check("failed turns stay out of the average", report.AvgGenTps.HasValue && Math.Abs(report.AvgGenTps!.Value - 50) < 1e-9,
            report.AvgGenTps?.ToString() ?? "null");
        h.Check("ttft averaged", report.AvgTtftMs.HasValue && Math.Abs(report.AvgTtftMs!.Value - 150) < 1e-9,
            report.AvgTtftMs?.ToString() ?? "null");
        h.Check("total duration includes the failed turn", Math.Abs(report.TotalDurationSeconds - 4.5) < 1e-9,
            report.TotalDurationSeconds.ToString());

        var empty = new PromptRunReport();
        h.Check("empty report has no averages", empty.AvgGenTps == null && empty.AvgPromptTps == null, "ok");

        h.Section("PromptRunDocument.BuildMarkdown");

        var run = new BenchmarkRun
        {
            Id = "2026-08-23_10-00-00",
            Label = "fa on",
            ProfileName = "Qwen",
            CreatedAt = new DateTime(2026, 8, 23, 10, 0, 0),
            ConfigSnapshot = new LlamaServerLauncher.Models.ServerConfiguration { ModelPath = @"C:\models\qwen.gguf" },
            PromptRun = report,
        };

        var md = Norm(PromptRunDocument.BuildMarkdown(run, report));
        h.Check("title carries the run name", md.StartsWith("# Prompt run: fa on (2026-08-23_10-00-00)"), md.Split('\n')[0]);
        h.Check("model name from the config", md.Contains("- Model: qwen.gguf"), "ok");
        h.Check("mode reported", md.Contains("- Mode: Conversation"), "ok");
        h.Check("max tokens reported", md.Contains("- Max tokens: 256"), "ok");
        h.Check("system prompt included", md.Contains("## System prompt") && md.Contains("You are terse."), "ok");
        h.Check("request headings numbered", md.Contains("## Request 1") && md.Contains("## Request 3"), "ok");
        h.Check("answer section present", md.Contains("### Answer"), "ok");
        h.Check("stats line built", md.Contains("Generation: 10 tok, 40.00 tok/s"), "ok");
        h.Check("failed request reported", md.Contains("**Failed**: boom"), "ok");
        h.Check("failed request has no answer heading after it",
            md.IndexOf("**Failed**", StringComparison.Ordinal) > md.IndexOf("### Answer", StringComparison.Ordinal), "ok");

        var fenced = new PromptRunReport();
        fenced.Turns.Add(new PromptRunTurn { Index = 1, Prompt = "```\ncode\n```", Response = "ok", DurationSeconds = 1 });
        var fencedMd = Norm(PromptRunDocument.BuildMarkdown(run, fenced));
        h.Check("fence grows past backticks in the prompt", fencedMd.Contains("````\n```\ncode\n```\n````"),
            fencedMd.Contains("````") ? "long fence used" : "fence too short");

        var reasoning = new PromptRunReport();
        reasoning.Turns.Add(new PromptRunTurn { Index = 1, Prompt = "q", Response = "a", Reasoning = "step one\nstep two", DurationSeconds = 1 });
        var reasoningMd = Norm(PromptRunDocument.BuildMarkdown(run, reasoning));
        h.Check("reasoning quoted", reasoningMd.Contains("> step one\n> step two"), "ok");

        h.Section("PromptRunDocument.BuildSummarySection");

        var summary = Norm(PromptRunDocument.BuildSummarySection(report));
        h.Check("summary is a table", summary.Contains("| --- | --- | --- | --- | --- | --- |"), "ok");
        h.Check("one row per request", summary.Split('\n').Count(l => l.StartsWith("| 1 |") || l.StartsWith("| 2 |") || l.StartsWith("| 3 |")) == 3, "ok");
        h.Check("summary points at the transcript", summary.Contains("prompt-run.md"), "ok");

        var emptySummary = Norm(PromptRunDocument.BuildSummarySection(new PromptRunReport()));
        h.Check("empty run says so", emptySummary.Contains("No requests were sent."), "ok");

        h.Section("ChatCompletionResponse.Parse");

        var body = @"{
            ""choices"": [ { ""finish_reason"": ""stop"", ""index"": 0,
                ""message"": { ""role"": ""assistant"", ""content"": ""Hi there"", ""reasoning_content"": ""thinking"" } } ],
            ""usage"": { ""prompt_tokens"": 11, ""completion_tokens"": 22 },
            ""timings"": { ""prompt_n"": 9, ""prompt_ms"": 120.5, ""prompt_per_second"": 74.7,
                           ""predicted_n"": 33, ""predicted_ms"": 700.0, ""predicted_per_second"": 47.1 }
        }";
        var parsed = ChatCompletionResponse.Parse(body);
        h.Check("content read", parsed.Content == "Hi there", parsed.Content);
        h.Check("reasoning read", parsed.Reasoning == "thinking", parsed.Reasoning ?? "null");
        h.Check("finish reason read", parsed.FinishReason == "stop", parsed.FinishReason ?? "null");
        h.Check("timings win over usage", parsed.PromptTokens == 9 && parsed.PredictedTokens == 33,
            $"{parsed.PromptTokens}/{parsed.PredictedTokens}");
        h.Check("speeds read", Math.Abs((parsed.GenTps ?? 0) - 47.1) < 1e-9 && Math.Abs((parsed.PromptMs ?? 0) - 120.5) < 1e-9, "ok");

        var noTimings = ChatCompletionResponse.Parse(
            @"{""choices"":[{""message"":{""content"":""x""}}],""usage"":{""prompt_tokens"":4,""completion_tokens"":5}}");
        h.Check("usage used when timings are missing", noTimings.PromptTokens == 4 && noTimings.PredictedTokens == 5,
            $"{noTimings.PromptTokens}/{noTimings.PredictedTokens}");
        h.Check("missing timings leave speeds unset", noTimings.GenTps == null, "ok");

        var emptyChoices = ChatCompletionResponse.Parse(@"{""choices"":[]}");
        h.Check("empty choices do not throw", emptyChoices.Content == "", "ok");

        h.Check("error object message extracted",
            ChatCompletionResponse.ExtractError(@"{""error"":{""code"":500,""message"":""no chat template""}}") == "no chat template", "ok");
        h.Check("error string extracted",
            ChatCompletionResponse.ExtractError(@"{""error"":""boom""}") == "boom", "ok");
        h.Check("non json error passed through",
            ChatCompletionResponse.ExtractError("plain failure") == "plain failure", "ok");

        h.Section("ModelListResponse");

        var ids = ModelListResponse.Parse(
            @"{""object"":""list"",""data"":[{""id"":""gemma-4-26B"",""object"":""model""},{""id"":""qwen3-30b"",""object"":""model""}]}");
        h.Check("ids read in order", ids.Count == 2 && ids[0] == "gemma-4-26B", string.Join(",", ids));
        h.Check("garbage gives no ids", ModelListResponse.Parse("not json").Count == 0, "ok");
        h.Check("missing data gives no ids", ModelListResponse.Parse(@"{""object"":""list""}").Count == 0, "ok");

        h.Check("nothing offered means no model name", ModelListResponse.Choose(new string[0], "x") == null, "ok");
        h.Check("exact name wins", ModelListResponse.Choose(ids, "qwen3-30b") == "qwen3-30b", "ok");
        h.Check("case ignored", ModelListResponse.Choose(ids, "QWEN3-30B") == "qwen3-30b", "ok");
        h.Check("substring of an id matches", ModelListResponse.Choose(ids, "qwen3") == "qwen3-30b", "ok");
        h.Check("id inside a longer profile name matches",
            ModelListResponse.Choose(ids, "gemma-4-26B-A4B-it-UD-Q8_K_XL") == "gemma-4-26B", "ok");
        h.Check("unrelated name falls back to the first", ModelListResponse.Choose(ids, "llama-3") == "gemma-4-26B", "ok");
        h.Check("no preference falls back to the first", ModelListResponse.Choose(ids, null) == "gemma-4-26B", "ok");

        h.Section("Prompt run in the benchmark report");

        var reportRun = new BenchmarkRun
        {
            Id = "2026-08-23_11-00-00",
            ProfileName = "Qwen",
            CreatedAt = new DateTime(2026, 8, 23, 11, 0, 0),
            Metrics = new BenchmarkMetrics
            {
                PromptRunTurns = 2,
                PromptRunGenTps = 50,
                PromptRunPromptTps = 1500,
                PromptRunTtftMs = 150,
            },
            PromptRun = report,
        };

        var runReport = Norm(BenchmarkReportBuilder.BuildRunReport(reportRun));
        h.Check("gen tok/s falls back to the prompt run", runReport.Contains("| Gen tok/s | 50.00 |"), "ok");
        h.Check("prompt tok/s falls back to the prompt run", runReport.Contains("| Prompt tok/s | 1500.00 |"), "ok");
        h.Check("ttft falls back to the prompt run", runReport.Contains("| TTFT, ms | 150.00 |"), "ok");
        h.Check("request count is a row", runReport.Contains("| Prompt run requests | 2 |"), "ok");
        h.Check("run report embeds the summary", runReport.Contains("## Prompt run"), "ok");
        h.Check("command section still last",
            runReport.IndexOf("## Command", StringComparison.Ordinal) > runReport.IndexOf("## Prompt run", StringComparison.Ordinal), "ok");

        var plainRun = new BenchmarkRun { Id = "x", ProfileName = "p", Metrics = new BenchmarkMetrics { StdGenTps = 12 } };
        var plainReport = Norm(BenchmarkReportBuilder.BuildRunReport(plainRun));
        h.Check("runs without a prompt run keep the old report", !plainReport.Contains("## Prompt run"), "ok");
        h.Check("standard workload still wins over the prompt run", plainReport.Contains("| Gen tok/s | 12.00 |"), "ok");
    }

    private static string Norm(string text) => text.Replace("\r\n", "\n");
}
