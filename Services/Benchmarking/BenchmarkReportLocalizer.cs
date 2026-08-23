using LlamaServerLauncher.Resources;

namespace LlamaServerLauncher.Services.Benchmarking;

public static class BenchmarkReportLocalizer
{
    public static string Localize(string label) => label switch
    {
        "Benchmark comparison" => LocalizedStrings.Instance.BenchmarkCompareTitle,
        "No benchmarks selected." => LocalizedStrings.Instance.BenchmarkReportNoSelection,
        "Metric" => LocalizedStrings.Instance.BenchmarkReportMetric,
        "Value" => LocalizedStrings.Instance.BenchmarkReportValue,
        "Profile" => LocalizedStrings.Instance.BenchmarkReportProfile,
        "Date" => LocalizedStrings.Instance.BenchmarkReportDate,
        "Model" => LocalizedStrings.Instance.BenchmarkReportModel,
        "Gen tok/s" => LocalizedStrings.Instance.BenchmarkReportGenTps,
        "Prompt tok/s" => LocalizedStrings.Instance.BenchmarkReportPromptTps,
        "TTFT, ms" => LocalizedStrings.Instance.BenchmarkReportTtft,
        "Duration, s" => LocalizedStrings.Instance.BenchmarkReportDuration,
        "Benchmark" => LocalizedStrings.Instance.BenchmarkReportTitle,
        "Command" => LocalizedStrings.Instance.BenchmarkReportCommand,
        "Hardware" => LocalizedStrings.Instance.BenchmarkReportHardware,
        "Draft model" => LocalizedStrings.Instance.BenchmarkReportDraftModel,
        "Custom args" => LocalizedStrings.Instance.BenchmarkReportCustomArgs,
        "Prompt run" => LocalizedStrings.Instance.BenchmarkReportPromptRun,
        "Prompt run requests" => LocalizedStrings.Instance.BenchmarkReportPromptRunRequests,
        "Mode" => LocalizedStrings.Instance.BenchmarkReportMode,
        "Conversation" => LocalizedStrings.Instance.BenchmarkReportModeConversation,
        "Independent requests" => LocalizedStrings.Instance.BenchmarkReportModeIndependent,
        "Max tokens" => LocalizedStrings.Instance.BenchmarkReportMaxTokens,
        "Server default" => LocalizedStrings.Instance.BenchmarkReportServerDefault,
        "System prompt" => LocalizedStrings.Instance.BenchmarkReportSystemPrompt,
        "Request" => LocalizedStrings.Instance.BenchmarkReportRequest,
        "Answer" => LocalizedStrings.Instance.BenchmarkReportAnswer,
        "Reasoning" => LocalizedStrings.Instance.BenchmarkReportReasoning,
        "Failed" => LocalizedStrings.Instance.BenchmarkReportFailed,
        "No requests were sent." => LocalizedStrings.Instance.BenchmarkReportNoRequests,
        "Tokens" => LocalizedStrings.Instance.BenchmarkReportTokens,
        "Full transcript: prompt-run.md" => LocalizedStrings.Instance.BenchmarkReportTranscriptHint,
        "Prompt" => LocalizedStrings.Instance.BenchmarkReportPrompt,
        "Generation" => LocalizedStrings.Instance.BenchmarkReportGeneration,
        "Finish reason" => LocalizedStrings.Instance.BenchmarkReportFinishReason,
        _ => label,
    };
}
