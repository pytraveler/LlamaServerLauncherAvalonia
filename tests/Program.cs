using System;

var harness = new Harness();
CommandLineTests.Run(harness);
OptimizationTests.Run(harness);
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
