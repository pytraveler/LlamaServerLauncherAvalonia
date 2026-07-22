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
        _ => label,
    };
}
