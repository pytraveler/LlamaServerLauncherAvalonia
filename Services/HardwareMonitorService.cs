using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LlamaServerLauncher.Models;

namespace LlamaServerLauncher.Services;

public sealed class HardwareMonitorService : IDisposable
{
    private const string NvidiaArgs =
        "--query-gpu=index,name,memory.total,memory.used,utilization.gpu,temperature.gpu --format=csv,noheader,nounits";
    private const string AmdArgs = "--showuse --showmeminfo vram --showtemp --json";

    private readonly LogService _log;
    private readonly int _intervalMs;
    private CancellationTokenSource? _cts;
    private (ulong Idle, ulong Total)? _prevCpu;
    private bool _nvidiaAvailable = true;
    private bool _amdAvailable = true;
    private volatile bool _gpuSuspended;

    public bool PollingSuspended
    {
        get => _gpuSuspended;
        set => _gpuSuspended = value;
    }

    public async Task<bool> WaitForGpuIdleAsync()
    {
        try
        {
            if (await _toolGate.WaitAsync(TimeSpan.FromSeconds(6)))
            {
                _toolGate.Release();
                return true;
            }
            return false;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }

    private readonly object _lock = new();
    private double? _cpu, _ramPct, _ramUsed, _ramTotal;
    private List<GpuInfo> _gpus = new();

    private readonly SemaphoreSlim _toolGate = new(1, 1);

    public event EventHandler<HardwareSnapshot>? Updated;

    public HardwareMonitorService(LogService log, int intervalMs = 2000)
    {
        _log = log;
        _intervalMs = intervalMs;
    }

    public void Start()
    {
        Stop();
        var cts = new CancellationTokenSource();
        _cts = cts;
        _prevCpu = null;
        lock (_lock)
        {
            _cpu = _ramPct = _ramUsed = _ramTotal = null;
            _gpus = new List<GpuInfo>();
        }
        _ = Task.Run(() => CpuRamLoopAsync(cts.Token));
        _ = Task.Run(() => GpuLoopAsync(cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    public void Dispose() => Stop();

    private async Task CpuRamLoopAsync(CancellationToken ct)
    {
        if (!SystemMetrics.CpuRamSupported) return;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (_gpuSuspended)
                {
                    await Task.Delay(_intervalMs, ct);
                    continue;
                }

                double? cpu = null, ramPct = null, ramUsed = null, ramTotal = null;

                if (SystemMetrics.ReadCpuTimes() is { } cur)
                {
                    if (_prevCpu is { } prev)
                        cpu = CpuUsage.Percent(prev.Idle, prev.Total, cur.Idle, cur.Total);
                    _prevCpu = cur;
                }
                if (SystemMetrics.ReadRam() is { } r)
                {
                    ramPct = r.Percent;
                    ramUsed = r.UsedGb;
                    ramTotal = r.TotalGb;
                }

                lock (_lock)
                {
                    _cpu = cpu;
                    _ramPct = ramPct;
                    _ramUsed = ramUsed;
                    _ramTotal = ramTotal;
                }
                Emit();

                await Task.Delay(_intervalMs, ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task GpuLoopAsync(CancellationToken ct)
    {
        var wasSuspended = false;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (!_nvidiaAvailable && !_amdAvailable)
                {
                    if (!SystemMetrics.CpuRamSupported)
                        Updated?.Invoke(this, HardwareSnapshot.Empty);
                    return;
                }

                var gpus = new List<GpuInfo>();
                var polled = false;
                await _toolGate.WaitAsync(ct);
                try
                {
                    if (!_gpuSuspended)
                    {
                        polled = true;
                        if (_nvidiaAvailable)
                        {
                            var output = await RunToolAsync("nvidia-smi", NvidiaArgs, ct);
                            if (output == null) _nvidiaAvailable = false;
                            else if (output.Length > 0) gpus.AddRange(GpuStatsParser.Parse(output));
                        }
                        if (_amdAvailable)
                        {
                            var output = await RunToolAsync("rocm-smi", AmdArgs, ct);
                            if (output == null) _amdAvailable = false;
                            else if (output.Length > 0) gpus.AddRange(AmdGpuParser.Parse(output));
                        }
                    }
                }
                finally
                {
                    _toolGate.Release();
                }

                if (polled)
                {
                    if (wasSuspended)
                    {
                        wasSuspended = false;
                        _log.Info("Hardware monitor: resumed");
                    }
                    if (gpus.Count > 0 || (!_nvidiaAvailable && !_amdAvailable))
                    {
                        lock (_lock) _gpus = gpus;
                        Emit();
                    }
                }
                else if (!wasSuspended)
                {
                    wasSuspended = true;
                    _log.Info("Hardware monitor: paused (server is loading a model)");
                }

                await Task.Delay(_intervalMs, ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    private void Emit()
    {
        HardwareSnapshot snap;
        lock (_lock)
        {
            snap = new HardwareSnapshot(_cpu, _ramPct, _ramUsed, _ramTotal, new List<GpuInfo>(_gpus));
        }
        Updated?.Invoke(this, snap);
    }

    private async Task<string?> RunToolAsync(string fileName, string args, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(4000, _intervalMs * 2)));
        var tct = timeout.Token;

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
            return null;
        }
        catch (Exception ex)
        {
            _log.Warning($"{fileName} failed to start: {ex.Message}");
            return null;
        }

        if (proc == null) return null;

        using (proc)
        {
            try
            {
                var outputTask = proc.StandardOutput.ReadToEndAsync(tct);
                await proc.WaitForExitAsync(tct);
                var output = await outputTask;
                int code;
                try { code = proc.ExitCode; } catch { code = -1; }
                return code == 0 ? output : "";
            }
            catch (OperationCanceledException)
            {
                if (ct.IsCancellationRequested) throw;
                try { if (!proc.HasExited) proc.Kill(true); } catch { }
                return "";
            }
            catch
            {
                return "";
            }
        }
    }
}
