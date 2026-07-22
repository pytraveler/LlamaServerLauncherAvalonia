namespace LlamaServerLauncher.Models;

public static class CpuUsage
{
    public static double Percent(ulong prevIdle, ulong prevTotal, ulong idle, ulong total)
    {
        if (total <= prevTotal) return 0;
        var dTotal = total - prevTotal;
        var dIdle = idle >= prevIdle ? idle - prevIdle : 0;
        var busy = dTotal >= dIdle ? dTotal - dIdle : 0;
        var pct = (double)busy / dTotal * 100.0;
        return pct < 0 ? 0 : pct > 100 ? 100 : pct;
    }
}
