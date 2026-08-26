using System.Collections.Generic;
using LlamaServerLauncher.Models;
using LlamaServerLauncher.Services;

public static class BackendAssetSelectorTests
{
    public static void Run(Harness h)
    {
        h.Section("BackendAssetSelector.PickBestIndex (Windows set)");
        var win = new List<string>
        {
            "llama-b6000-bin-win-cpu-x64.zip",
            "llama-b6000-bin-win-cuda-12.4-x64.zip",
            "llama-b6000-bin-win-cuda-11.7-x64.zip",
            "llama-b6000-bin-win-vulkan-x64.zip",
            "llama-b6000-bin-win-hip-radeon-x64.zip",
            "llama-b6000-bin-win-sycl-x64.zip",
            "llama-b6000-bin-win-arm64.zip",
        };

        Pick(h, "nvidia -> cuda", win, GpuVendor.Nvidia, false, "cuda");
        Pick(h, "amd -> vulkan", win, GpuVendor.Amd, false, "vulkan");
        Pick(h, "intel -> sycl", win, GpuVendor.Intel, false, "sycl");
        Pick(h, "none -> cpu", win, GpuVendor.None, false, "cpu");
        Pick(h, "unknown -> cpu", win, GpuVendor.Unknown, false, "cpu");

        h.Check("nvidia never picks arm on x64",
            !win[BackendAssetSelector.PickBestIndex(win, GpuVendor.Nvidia, false)].Contains("arm64"), "ok");
        h.Check("none never picks arm on x64",
            !win[BackendAssetSelector.PickBestIndex(win, GpuVendor.None, false)].Contains("arm64"), "ok");

        h.Section("BackendAssetSelector fallbacks");
        var noCuda = new List<string>
        {
            "llama-b6000-bin-win-cpu-x64.zip",
            "llama-b6000-bin-win-vulkan-x64.zip",
        };
        Pick(h, "nvidia w/o cuda -> vulkan", noCuda, GpuVendor.Nvidia, false, "vulkan");
        Pick(h, "amd w/o hip -> vulkan", noCuda, GpuVendor.Amd, false, "vulkan");

        var cpuOnly = new List<string> { "llama-b6000-bin-win-cpu-x64.zip" };
        Pick(h, "nvidia cpu-only -> cpu", cpuOnly, GpuVendor.Nvidia, false, "cpu");

        h.Section("BackendAssetSelector CPU sub-rank (old naming)");
        var oldCpu = new List<string>
        {
            "llama-b3000-bin-win-noavx-x64.zip",
            "llama-b3000-bin-win-avx512-x64.zip",
            "llama-b3000-bin-win-avx2-x64.zip",
            "llama-b3000-bin-win-avx-x64.zip",
        };
        Pick(h, "none prefers avx2", oldCpu, GpuVendor.None, false, "avx2");

        h.Section("BackendAssetSelector macOS arch");
        var mac = new List<string>
        {
            "llama-b6000-bin-macos-arm64.tar.gz",
            "llama-b6000-bin-macos-x64.tar.gz",
        };
        Pick(h, "apple silicon -> arm64", mac, GpuVendor.Apple, true, "arm64");
        Pick(h, "intel mac -> x64", mac, GpuVendor.Apple, false, "x64");

        h.Section("BackendAssetSelector arm64 host");
        Pick(h, "arm64 host prefers win arm build", win, GpuVendor.None, true, "arm64");

        h.Section("BackendAssetSelector labels");
        h.Check("cuda label", BackendAssetSelector.BackendLabel("llama-b1-bin-win-cuda-12.4-x64.zip") == "CUDA", BackendAssetSelector.BackendLabel("x-cuda-x"));
        h.Check("vulkan label", BackendAssetSelector.BackendLabel("x-vulkan-x64.zip") == "Vulkan", "ok");
        h.Check("hip label", BackendAssetSelector.BackendLabel("x-hip-radeon.zip") == "HIP (ROCm)", "ok");
        h.Check("cpu label", BackendAssetSelector.BackendLabel("x-cpu-x64.zip") == "CPU", "ok");
        h.Check("vendor nvidia", BackendAssetSelector.VendorLabel(GpuVendor.Nvidia) == "NVIDIA", "ok");
        h.Check("vendor none -> cpu", BackendAssetSelector.VendorLabel(GpuVendor.None) == "CPU", "ok");

        h.Section("BackendAssetSelector edge cases");
        h.Check("empty list -> -1", BackendAssetSelector.PickBestIndex(new List<string>(), GpuVendor.Nvidia, false) == -1, "ok");

        h.Section("LlamaCppDownloadService.ContainsPlatformBuild");
        h.Check("nothing published -> no builds",
            !LlamaCppDownloadService.ContainsPlatformBuild(Assets()), "ok");
        h.Check("null -> no builds",
            !LlamaCppDownloadService.ContainsPlatformBuild(null), "ok");
        h.Check("marker file only -> no builds",
            !LlamaCppDownloadService.ContainsPlatformBuild(Assets("nightly-tag.txt")), "ok");
        h.Check("cuda runtime alone is not a build",
            !LlamaCppDownloadService.ContainsPlatformBuild(Assets("cudart-llama-bin-win-cuda-12.4-x64.zip")), "ok");
        h.Check("windows build counts",
            LlamaCppDownloadService.ContainsPlatformBuild(Assets("llama-b10631-bin-win-cpu-x64.zip")), "ok");
        h.Check("build for another OS still counts",
            LlamaCppDownloadService.ContainsPlatformBuild(Assets("llama-b10631-bin-ubuntu-x64.tar.gz")), "ok");
        h.Check("macos build counts",
            LlamaCppDownloadService.ContainsPlatformBuild(Assets("llama-b10631-bin-macos-arm64.tar.gz")), "ok");
        h.Check("marker next to a build still counts",
            LlamaCppDownloadService.ContainsPlatformBuild(
                Assets("nightly-tag.txt", "llama-b10631-bin-win-cuda-12.4-x64.zip")), "ok");
    }

    private static List<ReleaseAsset> Assets(params string[] names)
    {
        var result = new List<ReleaseAsset>();
        foreach (var name in names)
            result.Add(new ReleaseAsset { Name = name });
        return result;
    }

    private static void Pick(Harness h, string label, List<string> names, GpuVendor vendor, bool isArm64, string expectedToken)
    {
        var idx = BackendAssetSelector.PickBestIndex(names, vendor, isArm64);
        var chosen = idx >= 0 && idx < names.Count ? names[idx] : "<none>";
        h.Check(label, chosen.Contains(expectedToken), chosen);
    }
}
