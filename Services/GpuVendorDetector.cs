using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LlamaServerLauncher.Models;

namespace LlamaServerLauncher.Services;

public static class GpuVendorDetector
{
    public static async Task<GpuVendor> DetectAsync(CancellationToken ct = default)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return GpuVendor.Apple;

            if (await ToolProducesOutputAsync("nvidia-smi", "--query-gpu=name --format=csv,noheader", ct))
                return GpuVendor.Nvidia;

            if (await ToolProducesOutputAsync("rocm-smi", "--showid", ct))
                return GpuVendor.Amd;
        }
        catch (OperationCanceledException) { }
        catch { }

        return GpuVendor.None;
    }

    private static async Task<bool> ToolProducesOutputAsync(string fileName, string args, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));

        Process? proc;
        try
        {
            proc = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch (Win32Exception)
        {
            return false;
        }
        catch
        {
            return false;
        }

        if (proc == null) return false;

        using (proc)
        {
            try
            {
                var outputTask = proc.StandardOutput.ReadToEndAsync(timeout.Token);
                await proc.WaitForExitAsync(timeout.Token);
                var output = await outputTask;
                int code;
                try { code = proc.ExitCode; } catch { code = -1; }
                return code == 0 && !string.IsNullOrWhiteSpace(output);
            }
            catch (OperationCanceledException)
            {
                try { if (!proc.HasExited) proc.Kill(true); } catch { }
                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
