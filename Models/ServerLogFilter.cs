using System;

namespace LlamaServerLauncher.Models;

public static class ServerLogFilter
{
    public static bool IsPollingNoise(string? line)
    {
        if (string.IsNullOrEmpty(line)) return false;

        return line.Contains("update_slots: all slots are idle")
            || line.Contains("done request: GET /slots")
            || line.Contains("done request: GET /health");
    }

    public static string? TryGetMcpProblem(string? line)
    {
        if (string.IsNullOrEmpty(line)) return null;

        int index = line.IndexOf("MCP", StringComparison.Ordinal);
        if (index < 0) return null;

        var message = line.Substring(index).Trim();

        bool isProblem = message.Contains("failed to spawn")
            || message.Contains("failed to start")
            || message.Contains("starting failed")
            || message.Contains("is no longer alive")
            || message.Contains("has no command")
            || message.Contains("duplicate server name")
            || message.Contains("no servers found")
            || message.Contains("unavailable")
            || message.Contains("skipping");

        return isProblem ? message : null;
    }
}
