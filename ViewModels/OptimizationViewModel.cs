using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LlamaServerLauncher.Models.Optimization;
using LlamaServerLauncher.Resources;
using LlamaServerLauncher.Services;
using LlamaServerLauncher.Services.Optimization;

namespace LlamaServerLauncher.ViewModels;

public class OptimizationViewModel : INotifyPropertyChanged
{
    public LocalizedStrings Localized { get; } = LocalizedStrings.Instance;

    private readonly Func<OptimizationResult, bool>? _applyToConfig;
    private readonly LogService? _log;
    private CancellationTokenSource? _cts;

    public event Action? RequestClose;

    public OptimizationViewModel(
        string? modelPath = null,
        string? serverExePath = null,
        Func<OptimizationResult, bool>? applyToConfig = null,
        LogService? log = null,
        string? contextSize = null,
        string? cacheTypeK = null,
        string? cacheTypeV = null,
        string? mmprojPath = null)
    {
        _applyToConfig = applyToConfig;
        _log = log;
        _modelPath = modelPath ?? "";
        _serverExePath = serverExePath ?? "";
        _contextSize = contextSize ?? "";
        _cacheTypeK = cacheTypeK ?? "";
        _cacheTypeV = cacheTypeV ?? "";
        _mmprojPath = mmprojPath ?? "";

        var def = SearchSpace.Default();
        _batchLow = def.Batch.Low.ToString(); _batchHigh = def.Batch.High.ToString();
        _ubatchLow = def.UBatch.Low.ToString(); _ubatchHigh = def.UBatch.High.ToString();
        _threadsLow = def.Threads.Low.ToString(); _threadsHigh = def.Threads.High.ToString();
        _gpuLayersLow = def.GpuLayers.Low.ToString(); _gpuLayersHigh = def.GpuLayers.High.ToString();
        _ncpuMoeLow = "0"; _ncpuMoeHigh = OptimizationConstants.DefaultMaxNCpuMoe.ToString();

        _runCommand = new AsyncRelayCommand(_ => RunAsync(), _ => !IsRunning);
        _stopCommand = new RelayCommand(_ => _cts?.Cancel(), _ => IsRunning);
        _applyCommand = new RelayCommand(_ => ApplyBest(), _ => Result != null && !IsRunning);
        _closeCommand = new RelayCommand(_ => RequestClose?.Invoke());

        RunCommand = new CommandAdapter(_runCommand);
        StopCommand = new CommandAdapter(_stopCommand);
        ApplyBestToConfigCommand = new CommandAdapter(_applyCommand);
        CloseCommand = new CommandAdapter(_closeCommand);
    }

    private string _modelPath;
    public string ModelPath { get => _modelPath; set => Set(ref _modelPath, value); }

    private string _serverExePath;
    public string ServerExePath { get => _serverExePath; set => Set(ref _serverExePath, value); }

    private string _llamaBenchPath = "";
    public string LlamaBenchPath { get => _llamaBenchPath; set => Set(ref _llamaBenchPath, value); }

    private int _metricIndex = 2;
    public int MetricIndex { get => _metricIndex; set => Set(ref _metricIndex, value); }

    private string _contextSize;
    public string ContextSize { get => _contextSize; set => Set(ref _contextSize, value); }

    private string _cacheTypeK;
    public string CacheTypeK { get => _cacheTypeK; set => Set(ref _cacheTypeK, value); }

    private string _cacheTypeV;
    public string CacheTypeV { get => _cacheTypeV; set => Set(ref _cacheTypeV, value); }

    private string _mmprojPath;
    public string MmprojPath { get => _mmprojPath; set => Set(ref _mmprojPath, value); }

    private int _overrideModeIndex;
    public int OverrideModeIndex { get => _overrideModeIndex; set => Set(ref _overrideModeIndex, value); }

    private string _nTrials = OptimizationConstants.DefaultNTrials.ToString();
    public string NTrials { get => _nTrials; set => Set(ref _nTrials, value); }

    private string _repeat = OptimizationConstants.DefaultRepeat.ToString();
    public string Repeat { get => _repeat; set => Set(ref _repeat, value); }

    private string _nTokens = OptimizationConstants.DefaultNTokens.ToString();
    public string NTokens { get => _nTokens; set => Set(ref _nTokens, value); }

    private string _nWarmupRuns = OptimizationConstants.DefaultWarmupRuns.ToString();
    public string NWarmupRuns { get => _nWarmupRuns; set => Set(ref _nWarmupRuns, value); }

    private bool _noWarmup;
    public bool NoWarmup { get => _noWarmup; set => Set(ref _noWarmup, value); }

    private string _nglMax = "";
    public string NglMax { get => _nglMax; set => Set(ref _nglMax, value); }

    private bool _useFit = true;
    public bool UseFit
    {
        get => _useFit;
        set { if (Set(ref _useFit, value)) OnPropertyChanged(nameof(CanTuneNgl)); }
    }

    public bool CanTuneNgl => !_useFit;

    private string _fitMargin = OptimizationConstants.DefaultFitMarginMiB.ToString();
    public string FitMargin { get => _fitMargin; set => Set(ref _fitMargin, value); }

    private bool _useHttpBenchmark;
    public bool UseHttpBenchmark { get => _useHttpBenchmark; set => Set(ref _useHttpBenchmark, value); }

    private bool _pruneBatchLtUbatch;
    public bool PruneBatchLtUbatch { get => _pruneBatchLtUbatch; set => Set(ref _pruneBatchLtUbatch, value); }

    private bool _estimateContext;
    public bool EstimateContext { get => _estimateContext; set => Set(ref _estimateContext, value); }

    private string _batchLow = "", _batchHigh = "", _ubatchLow = "", _ubatchHigh = "";
    private string _threadsLow = "", _threadsHigh = "", _gpuLayersLow = "", _gpuLayersHigh = "";
    public string BatchLow { get => _batchLow; set => Set(ref _batchLow, value); }
    public string BatchHigh { get => _batchHigh; set => Set(ref _batchHigh, value); }
    public string UBatchLow { get => _ubatchLow; set => Set(ref _ubatchLow, value); }
    public string UBatchHigh { get => _ubatchHigh; set => Set(ref _ubatchHigh, value); }
    public string ThreadsLow { get => _threadsLow; set => Set(ref _threadsLow, value); }
    public string ThreadsHigh { get => _threadsHigh; set => Set(ref _threadsHigh, value); }
    public string GpuLayersLow { get => _gpuLayersLow; set => Set(ref _gpuLayersLow, value); }
    public string GpuLayersHigh { get => _gpuLayersHigh; set => Set(ref _gpuLayersHigh, value); }

    private bool _tuneNCpuMoe;
    public bool TuneNCpuMoe { get => _tuneNCpuMoe; set => Set(ref _tuneNCpuMoe, value); }

    private string _ncpuMoeLow = "", _ncpuMoeHigh = "";
    public string NCpuMoeLow { get => _ncpuMoeLow; set => Set(ref _ncpuMoeLow, value); }
    public string NCpuMoeHigh { get => _ncpuMoeHigh; set => Set(ref _ncpuMoeHigh, value); }

    private bool _noMmap;
    public bool NoMmap { get => _noMmap; set => Set(ref _noMmap, value); }

    public ObservableCollection<TrialResult> Trials { get; } = new();

    private TrialResult? _bestTrial;
    public TrialResult? BestTrial { get => _bestTrial; private set => Set(ref _bestTrial, value); }
    private double _bestValue = double.NegativeInfinity;

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (Set(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(CanEditSettings));
                _runCommand.RaiseCanExecuteChanged();
                _stopCommand.RaiseCanExecuteChanged();
                _applyCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanEditSettings => !IsRunning;

    private string _progressText = "";
    public string ProgressText { get => _progressText; private set => Set(ref _progressText, value); }

    private string _statusMessage = "";
    public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }

    private OptimizationResult? _result;
    public OptimizationResult? Result
    {
        get => _result;
        private set
        {
            if (Set(ref _result, value))
            {
                OnPropertyChanged(nameof(HasResult));
                OnPropertyChanged(nameof(OptimizedServerCmd));
                OnPropertyChanged(nameof(OptimizedBenchCmd));
                OnPropertyChanged(nameof(DefaultBenchCmd));
                OnPropertyChanged(nameof(ImprovementText));
                OnPropertyChanged(nameof(RecommendedCtxText));
                _applyCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasResult => Result != null;
    public string OptimizedServerCmd => Result?.OptimizedServerCmd ?? "";
    public string OptimizedBenchCmd => Result?.OptimizedBenchCmd ?? "";
    public string DefaultBenchCmd => Result?.DefaultBenchCmd ?? "";

    public string ImprovementText
    {
        get
        {
            if (Result is not { } r) return "";
            string opt = r.OptimizedBenchValue?.ToString("F2", CultureInfo.InvariantCulture) ?? "?";
            string def = r.DefaultBenchValue?.ToString("F2", CultureInfo.InvariantCulture) ?? "?";
            string pct = r.ImprovementPct is { } p ? $"{p:+0.0;-0.0}%" : "n/a";
            return $"{opt} vs {def} tok/s  ({pct})";
        }
    }

    public string RecommendedCtxText =>
        Result?.RecommendedCtxSize is { } c ? c.ToString(CultureInfo.InvariantCulture) : "";

    private readonly AsyncRelayCommand _runCommand;
    private readonly RelayCommand _stopCommand;
    private readonly RelayCommand _applyCommand;
    private readonly RelayCommand _closeCommand;

    public System.Windows.Input.ICommand RunCommand { get; }
    public System.Windows.Input.ICommand StopCommand { get; }
    public System.Windows.Input.ICommand ApplyBestToConfigCommand { get; }
    public System.Windows.Input.ICommand CloseCommand { get; }

    public void Cancel() => _cts?.Cancel();

    private async Task RunAsync()
    {
        if (string.IsNullOrWhiteSpace(ModelPath))
        {
            StatusMessage = Localized.OptErrorNoModel;
            return;
        }

        Trials.Clear();
        Result = null;
        BestTrial = null;
        _bestValue = double.NegativeInfinity;
        StatusMessage = "";
        ProgressText = "";

        IsRunning = true;
        _cts = new CancellationTokenSource();
        var progress = new Progress<OptimizationProgress>(OnProgress);

        try
        {
            var opts = BuildOptions();
            var service = new OptimizationService(_log);
            var result = await service.RunAsync(opts, progress, _cts.Token);
            Result = result;
            ProgressText = Localized.OptDone;
            StatusMessage = Localized.OptDone;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = Localized.OptStopped;
            ProgressText = Localized.OptStopped;
        }
        catch (Exception ex)
        {
            _log?.Error($"Optimization failed: {ex}");
            StatusMessage = $"{Localized.OptError}: {ex.Message}";
            ProgressText = Localized.OptError;
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void OnProgress(OptimizationProgress p)
    {
        switch (p.Phase)
        {
            case OptimizationPhase.Stage1:
            case OptimizationPhase.Stage2:
            case OptimizationPhase.Stage3:
                if (p.Stage is { } stage && p.TrialNumber is { } num)
                {
                    var row = new TrialResult
                    {
                        Stage = stage,
                        Number = num,
                        Value = p.Value ?? 0,
                        PpValue = p.PpValue ?? 0,
                        Command = p.Command ?? "",
                    };
                    Trials.Add(row);

                    if (row.Value > _bestValue)
                    {
                        _bestValue = row.Value;
                        if (BestTrial != null) BestTrial.IsBest = false;
                        row.IsBest = true;
                        BestTrial = row;
                    }

                    ProgressText = string.Format(
                        CultureInfo.InvariantCulture,
                        Localized.OptStageProgressFormat,
                        stage, 3, p.TrialNumber, p.TrialsInStage ?? 0);
                }
                break;

            default:
                ProgressText = p.Message;
                break;
        }

        if (!string.IsNullOrEmpty(p.Message))
            StatusMessage = p.Message;
    }

    private OptimizationOptions BuildOptions()
    {
        var ss = new SearchSpace
        {
            Batch = (ParseInt(BatchLow, 8), ParseInt(BatchHigh, 16384)),
            UBatch = (ParseInt(UBatchLow, 4), ParseInt(UBatchHigh, 8192)),
            Threads = (ParseInt(ThreadsLow, 1), ParseInt(ThreadsHigh, Math.Max(1, Environment.ProcessorCount))),
            GpuLayers = (ParseInt(GpuLayersLow, 0), ParseInt(GpuLayersHigh, OptimizationConstants.DefaultMaxGpuLayers)),
            NCpuMoe = (ParseInt(NCpuMoeLow, 0), ParseInt(NCpuMoeHigh, OptimizationConstants.DefaultMaxNCpuMoe)),
        };

        return new OptimizationOptions
        {
            ModelPath = ModelPath,
            LlamaBenchPath = string.IsNullOrWhiteSpace(LlamaBenchPath) ? null : LlamaBenchPath,
            LlamaServerExecutablePath = string.IsNullOrWhiteSpace(ServerExePath) ? null : ServerExePath,
            Metric = MetricIndex switch { 1 => BenchMetric.Pp, 2 => BenchMetric.Mean, _ => BenchMetric.Tg },
            OverrideMode = OverrideModeIndex == 1 ? OverrideMode.Scan : OverrideMode.None,
            ContextSize = string.IsNullOrWhiteSpace(ContextSize) ? null : ParseInt(ContextSize, 0) is var cv && cv > 0 ? cv : null,
            CacheTypeK = string.IsNullOrWhiteSpace(CacheTypeK) ? null : CacheTypeK.Trim(),
            CacheTypeV = string.IsNullOrWhiteSpace(CacheTypeV) ? null : CacheTypeV.Trim(),
            MmprojPath = string.IsNullOrWhiteSpace(MmprojPath) ? null : MmprojPath.Trim(),
            NTrials = ParseInt(NTrials, OptimizationConstants.DefaultNTrials),
            Repeat = ParseInt(Repeat, OptimizationConstants.DefaultRepeat),
            NTokens = ParseInt(NTokens, OptimizationConstants.DefaultNTokens),
            NWarmupRuns = ParseInt(NWarmupRuns, OptimizationConstants.DefaultWarmupRuns),
            NoWarmup = NoWarmup,
            NglMax = string.IsNullOrWhiteSpace(NglMax) ? null : ParseInt(NglMax, OptimizationConstants.DefaultMaxGpuLayers),
            UseFit = UseFit,
            FitMarginMiB = ParseInt(FitMargin, OptimizationConstants.DefaultFitMarginMiB),
            SearchSpace = ss,
            UseHttpBenchmark = UseHttpBenchmark,
            TuneNCpuMoe = TuneNCpuMoe,
            NoMmap = NoMmap,
            PruneBatchLtUbatch = PruneBatchLtUbatch,
            EstimateContext = EstimateContext,
        };
    }

    private void ApplyBest()
    {
        if (Result is { } r)
        {
            bool applied = _applyToConfig?.Invoke(r) ?? false;
            StatusMessage = applied ? Localized.OptApplied : Localized.OptNoImprovementApply;
        }
    }

    private static int ParseInt(string s, int fallback) =>
        int.TryParse(s?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
