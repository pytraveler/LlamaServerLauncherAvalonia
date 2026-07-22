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
}
