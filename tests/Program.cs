using System;

var harness = new Harness();
CommandLineTests.Run(harness);
OptimizationTests.Run(harness);
ProxyProtocolTests.Run(harness);
GgufMetadataTests.Run(harness);
InferenceStatsParserTests.Run(harness);
GpuStatsParserTests.Run(harness);
CpuUsageTests.Run(harness);
AmdGpuParserTests.Run(harness);
ModelScanTests.Run(harness);
EndpointSnippetsTests.Run(harness);
BackendAssetSelectorTests.Run(harness);
ServerLogFilterTests.Run(harness);
McpConfigTests.Run(harness);
ServerCrashAdvisorTests.Run(harness);
ProcessExitCodeInfoTests.Run(harness);
NativeRuntimeProbeTests.Run(harness);
ReferencedPathScannerTests.Run(harness);
ProfileFavoritesTests.Run(harness);
ConfigurationDiffTests.Run(harness);
BenchmarkTests.Run(harness);
PromptRunTests.Run(harness);
GitHubReleaseSourceTests.Run(harness);
LocalAddressTests.Run(harness);
AppUpdateDecisionTests.Run(harness);
LlamaBuildBackupTests.Run(harness);
return harness.Report();

public sealed class Harness
{
    private int _failures;
    private int _total;

    public void Section(string name) => Console.WriteLine($"\n=== {name} ===");

    public void Check(string label, bool ok, string detail)
    {
        _total++;
        if (!ok) _failures++;
        Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {label}: {detail}");
    }

    public int Report()
    {
        Console.WriteLine($"\n{_total - _failures}/{_total} passed");
        Console.WriteLine(_failures == 0 ? "ALL PASS" : $"{_failures} FAILED");
        return _failures == 0 ? 0 : 1;
    }
}
