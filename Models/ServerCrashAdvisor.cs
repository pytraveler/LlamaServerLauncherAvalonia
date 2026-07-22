namespace LlamaServerLauncher.Models;

public static class ServerCrashAdvisor
{
    public static bool IsPinnedMemoryFailure(string? line) =>
        line != null
        && line.Contains("pinned memory")
        && (line.Contains("failed to allocate") || line.Contains("resource already mapped"));

    public static bool IsCudaInitFailure(string? line) =>
        line != null && line.Contains("CUDA error: shared object initialization failed");

    public static bool ShouldSuggestDisableNoMmap(string? line, bool noMmapEnabled) =>
        noMmapEnabled && (IsPinnedMemoryFailure(line) || IsCudaInitFailure(line));
}
