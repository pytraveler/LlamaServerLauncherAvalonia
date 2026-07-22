using System.Collections.Generic;

namespace LlamaServerLauncher.Models;

public enum GpuVendor { Unknown, None, Nvidia, Amd, Intel, Apple }

public static class BackendAssetSelector
{
    private enum Backend { Cuda, Vulkan, Hip, Sycl, Cpu, MacArm, MacX64, Other }

    public static int PickBestIndex(IReadOnlyList<string> assetNames, GpuVendor vendor, bool isArm64)
    {
        if (assetNames == null || assetNames.Count == 0) return -1;
        int best = 0;
        int bestScore = int.MinValue;
        for (int i = 0; i < assetNames.Count; i++)
        {
            int s = Score(assetNames[i], vendor, isArm64);
            if (s > bestScore)
            {
                bestScore = s;
                best = i;
            }
        }
        return best;
    }

    public static int Score(string assetName, GpuVendor vendor, bool isArm64)
    {
        var n = (assetName ?? "").ToLowerInvariant();
        var b = Detect(n);

        bool assetArm = n.Contains("arm64");
        if (b != Backend.MacArm && b != Backend.MacX64 && assetArm && !isArm64)
            return 0;

        if (b == Backend.MacArm) return (isArm64 ? 100 : 60) * 10;
        if (b == Backend.MacX64) return (isArm64 ? 55 : 100) * 10;

        int major = Pref(vendor, b);
        int sub = b == Backend.Cpu ? CpuSubRank(n) : 0;
        if (isArm64 && assetArm && b == Backend.Cpu) sub += 3;
        return major * 10 + sub;
    }

    public static string BackendLabel(string assetName) => Detect((assetName ?? "").ToLowerInvariant()) switch
    {
        Backend.Cuda => "CUDA",
        Backend.Vulkan => "Vulkan",
        Backend.Hip => "HIP (ROCm)",
        Backend.Sycl => "SYCL",
        Backend.MacArm => "macOS arm64 (Metal)",
        Backend.MacX64 => "macOS x64 (Metal)",
        _ => "CPU"
    };

    public static string VendorLabel(GpuVendor vendor) => vendor switch
    {
        GpuVendor.Nvidia => "NVIDIA",
        GpuVendor.Amd => "AMD",
        GpuVendor.Intel => "Intel",
        GpuVendor.Apple => "Apple",
        _ => "CPU"
    };

    private static int Pref(GpuVendor v, Backend b) => v switch
    {
        GpuVendor.Nvidia => b switch { Backend.Cuda => 100, Backend.Vulkan => 70, Backend.Cpu => 40, Backend.Sycl => 15, Backend.Hip => 10, _ => 5 },
        GpuVendor.Amd => b switch { Backend.Vulkan => 100, Backend.Hip => 90, Backend.Cpu => 40, Backend.Sycl => 15, Backend.Cuda => 0, _ => 5 },
        GpuVendor.Intel => b switch { Backend.Sycl => 100, Backend.Vulkan => 90, Backend.Cpu => 40, Backend.Cuda => 0, Backend.Hip => 0, _ => 5 },
        _ => b switch { Backend.Cpu => 100, Backend.Vulkan => 20, Backend.Sycl => 10, Backend.Hip => 5, Backend.Cuda => 0, _ => 10 }
    };

    private static int CpuSubRank(string n)
    {
        if (n.Contains("-cpu-") || n.Contains("-cpu.")) return 5;
        if (n.Contains("noavx")) return 0;
        if (n.Contains("avx512")) return 3;
        if (n.Contains("avx2")) return 4;
        if (n.Contains("avx")) return 2;
        if (n.Contains("sse")) return 1;
        return 4;
    }

    private static Backend Detect(string n)
    {
        if (n.Contains("cuda") || n.Contains("cu11") || n.Contains("cu12") || n.Contains("cu13")) return Backend.Cuda;
        if (n.Contains("vulkan")) return Backend.Vulkan;
        if (n.Contains("hip") || n.Contains("rocm")) return Backend.Hip;
        if (n.Contains("sycl")) return Backend.Sycl;
        if (n.Contains("macos"))
            return n.Contains("arm64") ? Backend.MacArm : Backend.MacX64;
        return Backend.Cpu;
    }
}
