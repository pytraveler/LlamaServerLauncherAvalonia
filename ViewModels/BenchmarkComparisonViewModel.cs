using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using LlamaServerLauncher.Controls;
using LlamaServerLauncher.Models.Benchmarking;
using LlamaServerLauncher.Resources;
using LlamaServerLauncher.Services;
using LlamaServerLauncher.Services.Benchmarking;

namespace LlamaServerLauncher.ViewModels;

public sealed class BenchmarkRunRow : INotifyPropertyChanged
{
    private readonly Action _onChanged;
    private bool _isSelected;

    public BenchmarkRun Run { get; }

    public BenchmarkRunRow(BenchmarkRun run, Action onChanged)
    {
        Run = run;
        _onChanged = onChanged;
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
            _onChanged();
        }
    }

    public string Title =>
        $"{Run.ProfileName} — {(string.IsNullOrWhiteSpace(Run.Label) ? Run.Id : Run.Label)}";

    public string Subtitle
    {
        get
        {
            var g = Run.Metrics.StdGenTps ?? Run.Metrics.LogGenTps ?? Run.Metrics.PredictedTokensSeconds;
            var tps = g.HasValue ? $" · {g.Value:F1} tok/s" : string.Empty;
            return $"{Run.CreatedAt:yyyy-MM-dd HH:mm}{tps}";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class ProfileFilterRow : INotifyPropertyChanged
{
    private readonly Action _onChanged;
    private bool _isChecked;

    public string Name { get; }

    public ProfileFilterRow(string name, bool isChecked, Action onChanged)
    {
        Name = name;
        _isChecked = isChecked;
        _onChanged = onChanged;
    }

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value) return;
            _isChecked = value;
            OnPropertyChanged();
            _onChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class BenchmarkComparisonViewModel : INotifyPropertyChanged
{
    private readonly BenchmarkStorageService _storage;
    private readonly LogService _log;

    private List<BenchmarkComparisonSet> _sets = new();
    private readonly List<BenchmarkRunRow> _allRows = new();
    private Control _comparisonView = new StackPanel();
    private string _comparisonMarkdown = string.Empty;
    private string _setName = string.Empty;
    private string? _selectedSetName;
    private bool _suppressSetLoad;
    private bool _suppressFilter;

    public LocalizedStrings Localized => LocalizedStrings.Instance;
    public event Action? RequestClose;
    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<BenchmarkRunRow> Runs { get; } = new();
    public ObservableCollection<ProfileFilterRow> ProfileFilters { get; } = new();
    public ObservableCollection<string> SavedSetNames { get; } = new();

    public BenchmarkComparisonViewModel(BenchmarkStorageService storage, LogService log)
    {
        _storage = storage;
        _log = log;
        LocalizedStrings.CultureChanged += OnCultureChanged;
        Refresh();
        LoadSets();
    }

    public void Detach() => LocalizedStrings.CultureChanged -= OnCultureChanged;

    private void OnCultureChanged() => UpdateComparison();

    public Control ComparisonView
    {
        get => _comparisonView;
        private set { _comparisonView = value; OnPropertyChanged(); }
    }

    public string SetName
    {
        get => _setName;
        set { if (_setName != value) { _setName = value; OnPropertyChanged(); } }
    }

    public string? SelectedSetName
    {
        get => _selectedSetName;
        set
        {
            if (_selectedSetName == value) return;
            _selectedSetName = value;
            OnPropertyChanged();
            if (!_suppressSetLoad && value != null)
                LoadSet(value);
        }
    }

    public bool HasSelection => Runs.Any(r => r.IsSelected);
    public bool HasRuns => Runs.Count > 0;

    public void Refresh()
    {
        var previouslySelected = _allRows.Where(r => r.IsSelected)
            .Select(r => Key(r.Run)).ToHashSet();
        var uncheckedProfiles = ProfileFilters.Where(f => !f.IsChecked)
            .Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        _allRows.Clear();
        foreach (var run in _storage.LoadAllRuns())
        {
            var row = new BenchmarkRunRow(run, OnRowSelectionChanged);
            if (previouslySelected.Contains(Key(run)))
                row.IsSelected = true;
            _allRows.Add(row);
        }

        _suppressFilter = true;
        ProfileFilters.Clear();
        foreach (var name in _allRows.Select(r => r.Run.ProfileName)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            ProfileFilters.Add(new ProfileFilterRow(name, !uncheckedProfiles.Contains(name), OnFilterChanged));
        _suppressFilter = false;

        ApplyFilter();
    }

    private void OnFilterChanged()
    {
        if (!_suppressFilter)
            ApplyFilter();
    }

    private void ApplyFilter()
    {
        var allowed = ProfileFilters.Where(f => f.IsChecked)
            .Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Runs.Clear();
        foreach (var row in _allRows)
            if (allowed.Contains(row.Run.ProfileName))
                Runs.Add(row);
        OnPropertyChanged(nameof(HasRuns));
        UpdateComparison();
    }

    public void SelectAllProfiles()
    {
        _suppressFilter = true;
        foreach (var f in ProfileFilters)
            f.IsChecked = true;
        _suppressFilter = false;
        ApplyFilter();
    }

    private void OnRowSelectionChanged() => UpdateComparison();

    private void UpdateComparison()
    {
        var selected = Runs.Where(r => r.IsSelected).Select(r => r.Run).ToList();
        _comparisonMarkdown = BenchmarkReportBuilder.BuildComparison(selected, BenchmarkReportLocalizer.Localize);
        ComparisonView = MarkdownRenderer.Render(_comparisonMarkdown);
        OnPropertyChanged(nameof(HasSelection));
    }

    private void LoadSets()
    {
        _sets = _storage.LoadComparisons();
        SavedSetNames.Clear();
        foreach (var s in _sets)
            SavedSetNames.Add(s.Name);
    }

    private void LoadSet(string name)
    {
        var set = _sets.FirstOrDefault(s => s.Name == name);
        if (set == null) return;
        var wanted = set.Runs
            .Select(r => $"{r.ProfileName}|{r.RunId}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _suppressFilter = true;
        foreach (var f in ProfileFilters)
            f.IsChecked = true;
        _suppressFilter = false;
        foreach (var row in _allRows)
            row.IsSelected = wanted.Contains(Key(row.Run));
        ApplyFilter();
        SetName = name;
    }

    public async Task SaveSetAsync()
    {
        if (string.IsNullOrWhiteSpace(SetName))
            return;

        var refs = Runs.Where(r => r.IsSelected)
            .Select(r => new BenchmarkRunRef { ProfileName = r.Run.ProfileName, RunId = r.Run.Id })
            .ToList();

        var existing = _sets.FirstOrDefault(s => string.Equals(s.Name, SetName, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            existing.Runs = refs;
        else
            _sets.Add(new BenchmarkComparisonSet { Name = SetName.Trim(), Runs = refs });

        await _storage.SaveComparisonsAsync(_sets);
        LoadSets();
        _suppressSetLoad = true;
        SelectedSetName = _sets.FirstOrDefault(s => string.Equals(s.Name, SetName, StringComparison.OrdinalIgnoreCase))?.Name;
        _suppressSetLoad = false;
    }

    public async Task DeleteSetAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedSetName))
            return;
        _sets.RemoveAll(s => string.Equals(s.Name, SelectedSetName, StringComparison.OrdinalIgnoreCase));
        await _storage.SaveComparisonsAsync(_sets);
        LoadSets();
        SelectedSetName = null;
    }

    public async Task ExportMarkdownAsync()
    {
        if (!HasSelection) return;
        var path = await WindowsFileDialogs.SaveFileDialogAsync(
            LocalizedStrings.Instance.BenchmarkExportMd, "md",
            new[] { ("Markdown", new[] { "*.md" }) });
        if (string.IsNullOrEmpty(path)) return;
        await System.IO.File.WriteAllTextAsync(path, _comparisonMarkdown);
    }

    public async Task ExportZipAsync()
    {
        if (!HasSelection) return;
        var selected = Runs.Where(r => r.IsSelected).Select(r => r.Run).ToList();
        var path = await WindowsFileDialogs.SaveFileDialogAsync(
            LocalizedStrings.Instance.BenchmarkExportZip, "zip",
            new[] { ("ZIP", new[] { "*.zip" }) });
        if (string.IsNullOrEmpty(path)) return;
        await _storage.ExportRunsToZipAsync(selected, _comparisonMarkdown, path);
    }

    public void RevealRun(BenchmarkRunRow row)
    {
        if (!string.IsNullOrEmpty(row.Run.DirectoryPath))
            ShellHelper.RevealInExplorer(row.Run.DirectoryPath);
    }

    public async Task PinFilesAsync(BenchmarkRunRow row)
    {
        var paths = await WindowsFileDialogs.OpenFileDialogAsync(
            LocalizedStrings.Instance.BenchmarkPinFiles, allowMultiple: true);
        await PinAsync(row, paths);
    }

    public async Task PinFolderAsync(BenchmarkRunRow row)
    {
        var path = await WindowsFileDialogs.OpenFolderDialogAsync(LocalizedStrings.Instance.BenchmarkPinFolder);
        await PinAsync(row, string.IsNullOrEmpty(path) ? null : new[] { path });
    }

    private async Task PinAsync(BenchmarkRunRow row, string[]? paths)
    {
        if (paths == null || paths.Length == 0)
            return;
        try
        {
            var copied = await _storage.CopyIntoPinnedAsync(row.Run, paths);
            if (copied > 0)
                ShellHelper.RevealInExplorer(_storage.GetPinnedDir(row.Run));
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to pin files: {ex.Message}");
        }
    }

    public void DeleteSelectedRuns()
    {
        var selected = Runs.Where(r => r.IsSelected).Select(r => r.Run).ToList();
        foreach (var run in selected)
            _storage.DeleteRun(run);
        Refresh();
    }

    public void Close() => RequestClose?.Invoke();

    private static string Key(BenchmarkRun run) => $"{run.ProfileName}|{run.Id}";

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
