using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using LlamaServerLauncher.Services;

public static class CudaRuntimeAssetTests
{
    private static readonly string[] ReleaseB11002 =
    {
        "cudart-llama-b11002-bin-ubuntu-cuda-12.8-x64.tar.gz",
        "cudart-llama-b11002-bin-ubuntu-cuda-13.3-arm64.tar.gz",
        "cudart-llama-b11002-bin-ubuntu-cuda-13.3-x64.tar.gz",
        "cudart-llama-bin-win-cuda-12.4-x64.zip",
        "cudart-llama-bin-win-cuda-13.4-arm64.zip",
        "cudart-llama-bin-win-cuda-13.4-x64.zip",
        "llama-b11002-bin-macos-arm64.tar.gz",
        "llama-b11002-bin-ubuntu-cuda-13.3-x64.tar.gz",
        "llama-b11002-bin-win-cpu-x64.zip",
        "llama-b11002-bin-win-cuda-12.4-x64.zip",
        "llama-b11002-bin-win-cuda-13.4-arm64.zip",
        "llama-b11002-bin-win-cuda-13.4-x64.zip",
        "llama-b11002-bin-win-vulkan-x64.zip",
    };

    private static List<ReleaseAsset> Assets(IEnumerable<string> names) =>
        names.Select(n => new ReleaseAsset { Name = n, DownloadUrl = "https://example/" + n }).ToList();

    private static string? Match(LlamaCppDownloadService service, string selected, IEnumerable<string> all)
    {
        var assets = Assets(all);
        var asset = assets.First(a => a.Name == selected);
        return service.FindMatchingCudaDllAsset(asset, assets)?.Name;
    }

    public static void Run(Harness h)
    {
        h.Section("cudart asset matching");

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            h.Check("cudart matching is windows-only", true, "skipped off windows");
            return;
        }

        var dataDir = Path.Combine(Path.GetTempPath(), "llama-cudart-tests", Guid.NewGuid().ToString("N"));
        var service = new LlamaCppDownloadService(dataDir);

        var cuda134 = Match(service, "llama-b11002-bin-win-cuda-13.4-x64.zip", ReleaseB11002);
        h.Check("cuda 13.4 x64 takes the windows runtime zip",
            cuda134 == "cudart-llama-bin-win-cuda-13.4-x64.zip", cuda134 ?? "null");

        var cuda124 = Match(service, "llama-b11002-bin-win-cuda-12.4-x64.zip", ReleaseB11002);
        h.Check("cuda 12.4 x64 takes its own runtime zip",
            cuda124 == "cudart-llama-bin-win-cuda-12.4-x64.zip", cuda124 ?? "null");

        var arm = Match(service, "llama-b11002-bin-win-cuda-13.4-arm64.zip", ReleaseB11002);
        h.Check("arm64 build does not take the x64 runtime",
            arm == "cudart-llama-bin-win-cuda-13.4-arm64.zip", arm ?? "null");

        var vulkan = Match(service, "llama-b11002-bin-win-vulkan-x64.zip", ReleaseB11002);
        h.Check("a non-cuda build asks for no runtime", vulkan == null, vulkan ?? "null");

        var noWindowsRuntime = Assets(ReleaseB11002.Where(n => !n.Contains("-win-") || !n.StartsWith("cudart-")));
        var selected = noWindowsRuntime.First(a => a.Name == "llama-b11002-bin-win-cuda-13.4-x64.zip");
        var fallback = service.FindMatchingCudaDllAsset(selected, noWindowsRuntime)?.Name;
        h.Check("a linux runtime tarball is never picked for windows", fallback == null, fallback ?? "null");

        try { Directory.Delete(dataDir, true); } catch { }
    }
}
