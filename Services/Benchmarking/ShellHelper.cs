using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace LlamaServerLauncher.Services.Benchmarking;

public static class ShellHelper
{
    public static void RevealInExplorer(string path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (File.Exists(path))
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                else
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                if (File.Exists(path))
                    Process.Start(new ProcessStartInfo("open", $"-R \"{path}\""));
                else
                    Process.Start(new ProcessStartInfo("open", $"\"{path}\""));
            }
            else
            {
                var dir = Directory.Exists(path) ? path : (Path.GetDirectoryName(path) ?? path);
                Process.Start(new ProcessStartInfo("xdg-open", $"\"{dir}\""));
            }
        }
        catch
        {
        }
    }
}
