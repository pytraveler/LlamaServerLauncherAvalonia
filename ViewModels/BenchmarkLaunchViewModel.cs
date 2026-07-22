using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using LlamaServerLauncher.Models;
using LlamaServerLauncher.Resources;

namespace LlamaServerLauncher.ViewModels;

public sealed class BenchmarkLaunchResult
{
    public string FinalArgs { get; init; } = string.Empty;
    public bool RunStandardWorkload { get; init; }
    public bool StopAfterWorkload { get; init; }
    public int StdPromptTokens { get; init; }
    public int StdNPredict { get; init; }
    public int StdRepeat { get; init; }
    public string Label { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
}

public sealed class BenchmarkLaunchViewModel : INotifyPropertyChanged
{
    private readonly int _seedValue;
    private readonly Func<string, string> _previewBuilder;
    private readonly Action<BenchmarkLaunchResult> _onRun;
    private bool _suppressSync;

    private string _argsText = string.Empty;
    private bool _fixSeed = true;
    private bool _enableMetrics = true;
    private bool _runStandardWorkload;
    private bool _stopAfterWorkload;
    private int _stdPromptTokens = 512;
    private int _stdNPredict = 128;
    private int _stdRepeat = 3;
    private string _label = string.Empty;
    private string _notes = string.Empty;
    private string _commandPreview = string.Empty;

    public LocalizedStrings Localized => LocalizedStrings.Instance;
    public bool MetricsSupported { get; }

    public event Action? RequestClose;
    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<CustomArgumentItem> ArgItems { get; } = new();

    public BenchmarkLaunchViewModel(
        string initialArgs,
        int seedValue,
        bool metricsSupported,
        Func<string, string> previewBuilder,
        Action<BenchmarkLaunchResult> onRun)
    {
        _seedValue = seedValue;
        _previewBuilder = previewBuilder;
        _onRun = onRun;
        MetricsSupported = metricsSupported;
        _enableMetrics = metricsSupported;
        _argsText = initialArgs ?? string.Empty;
        RebuildArgItems();
        RebuildPreview();
    }

    public string ArgsText
    {
        get => _argsText;
        set
        {
            if (_argsText == value) return;
            _argsText = value;
            OnPropertyChanged();
            if (!_suppressSync)
            {
                RebuildArgItems();
                RebuildPreview();
            }
        }
    }

    public bool FixSeed
    {
        get => _fixSeed;
        set { if (_fixSeed != value) { _fixSeed = value; OnPropertyChanged(); RebuildPreview(); } }
    }

    public bool EnableMetrics
    {
        get => _enableMetrics;
        set { if (_enableMetrics != value) { _enableMetrics = value; OnPropertyChanged(); RebuildPreview(); } }
    }

    public bool RunStandardWorkload
    {
        get => _runStandardWorkload;
        set { if (_runStandardWorkload != value) { _runStandardWorkload = value; OnPropertyChanged(); } }
    }

    public bool StopAfterWorkload
    {
        get => _stopAfterWorkload;
        set { if (_stopAfterWorkload != value) { _stopAfterWorkload = value; OnPropertyChanged(); } }
    }

    public int StdPromptTokens
    {
        get => _stdPromptTokens;
        set { if (_stdPromptTokens != value) { _stdPromptTokens = value; OnPropertyChanged(); } }
    }

    public int StdNPredict
    {
        get => _stdNPredict;
        set { if (_stdNPredict != value) { _stdNPredict = value; OnPropertyChanged(); } }
    }

    public int StdRepeat
    {
        get => _stdRepeat;
        set { if (_stdRepeat != value) { _stdRepeat = value; OnPropertyChanged(); } }
    }

    public string Label
    {
        get => _label;
        set { if (_label != value) { _label = value; OnPropertyChanged(); } }
    }

    public string Notes
    {
        get => _notes;
        set { if (_notes != value) { _notes = value; OnPropertyChanged(); } }
    }

    public string CommandPreview
    {
        get => _commandPreview;
        private set { if (_commandPreview != value) { _commandPreview = value; OnPropertyChanged(); } }
    }

    public bool HasArgItems => ArgItems.Count > 0;

    public void OnToggleChanged() => RebuildPreview();

    public void RemoveArgItem(CustomArgumentItem item)
    {
        if (!ArgItems.Remove(item))
            return;
        _suppressSync = true;
        ArgsText = string.Join(" ", ArgItems.Select(a => a.OriginalArg));
        _suppressSync = false;
        OnPropertyChanged(nameof(HasArgItems));
        RebuildPreview();
    }

    public void Run()
    {
        var result = new BenchmarkLaunchResult
        {
            FinalArgs = BuildFinalArgs(),
            RunStandardWorkload = _runStandardWorkload,
            StopAfterWorkload = _stopAfterWorkload,
            StdPromptTokens = Math.Max(1, _stdPromptTokens),
            StdNPredict = Math.Max(1, _stdNPredict),
            StdRepeat = Math.Max(1, _stdRepeat),
            Label = _label ?? string.Empty,
            Notes = _notes ?? string.Empty,
        };
        _onRun(result);
        RequestClose?.Invoke();
    }

    public void Cancel() => RequestClose?.Invoke();

    private string BuildFinalArgs()
    {
        string baseArgs = ArgItems.Count > 0 && ArgItems.All(a => a.IsEnabled)
            ? (_argsText ?? string.Empty).Trim()
            : string.Join(" ", ArgItems.Where(a => a.IsEnabled).Select(a => a.OriginalArg));

        var tokens = CommandLineParser.ParseArguments(baseArgs);
        var flags = new HashSet<string>(tokens.Where(CommandLineParser.IsFlag), StringComparer.OrdinalIgnoreCase);

        if (_enableMetrics && MetricsSupported && !flags.Contains("--metrics"))
            baseArgs = AppendToken(baseArgs, "--metrics");
        if (_fixSeed && !flags.Contains("-s") && !flags.Contains("--seed"))
            baseArgs = AppendToken(baseArgs, $"-s {_seedValue}");

        return baseArgs.Trim();
    }

    private static string AppendToken(string args, string token) =>
        string.IsNullOrWhiteSpace(args) ? token : args.Trim() + " " + token;

    private void RebuildPreview()
    {
        var final = BuildFinalArgs();
        try
        {
            CommandPreview = _previewBuilder(final);
        }
        catch
        {
            CommandPreview = final;
        }
    }

    private void RebuildArgItems()
    {
        var groups = TokenizeGroups(_argsText);
        var prev = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var it in ArgItems)
            prev.TryAdd(it.OriginalArg, it.IsEnabled);

        ArgItems.Clear();
        foreach (var g in groups)
        {
            bool enabled = prev.TryGetValue(g, out var e) ? e : true;
            ArgItems.Add(new CustomArgumentItem { Name = g, OriginalArg = g, IsEnabled = enabled });
        }
        OnPropertyChanged(nameof(HasArgItems));
    }

    private static List<string> TokenizeGroups(string args)
    {
        var groups = new List<string>();
        var tokens = CommandLineParser.ParseArguments(args ?? string.Empty);
        int i = 0;
        while (i < tokens.Count)
        {
            var t = tokens[i];
            if (CommandLineParser.IsFlag(t) && i + 1 < tokens.Count && !CommandLineParser.IsFlag(tokens[i + 1]))
            {
                groups.Add(Quote(t) + " " + Quote(tokens[i + 1]));
                i += 2;
            }
            else
            {
                groups.Add(Quote(t));
                i++;
            }
        }
        return groups;
    }

    private static string Quote(string token)
    {
        if (string.IsNullOrEmpty(token))
            return "\"\"";
        return token.IndexOf(' ') >= 0 ? $"\"{token}\"" : token;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
