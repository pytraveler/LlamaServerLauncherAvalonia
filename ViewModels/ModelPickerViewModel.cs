using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LlamaServerLauncher.Models;
using LlamaServerLauncher.Resources;
using LlamaServerLauncher.Services;

namespace LlamaServerLauncher.ViewModels;

public class ModelPickerViewModel : INotifyPropertyChanged
{
    public LocalizedStrings Localized { get; } = LocalizedStrings.Instance;

    private readonly List<ModelScanEntry> _all = new();
    public ObservableCollection<ModelScanEntry> Models { get; } = new();

    private readonly VramBudget? _budget;
    private CancellationTokenSource? _scanCts;

    public event Action? RequestClose;

    public ModelPickerViewModel(string initialFolder, bool recursive, VramBudget? budget = null)
    {
        _folderPath = initialFolder ?? "";
        _recursive = recursive;
        _budget = budget;
    }

    private string _folderPath = "";
    public string FolderPath
    {
        get => _folderPath;
        set { if (_folderPath != value) { _folderPath = value; OnPropertyChanged(); } }
    }

    private bool _recursive;
    public bool Recursive
    {
        get => _recursive;
        set { if (_recursive != value) { _recursive = value; OnPropertyChanged(); } }
    }

    private string _filterText = "";
    public string FilterText
    {
        get => _filterText;
        set { if (_filterText != value) { _filterText = value; OnPropertyChanged(); ApplyFilter(); } }
    }

    private ModelScanEntry? _selectedModel;
    public ModelScanEntry? SelectedModel
    {
        get => _selectedModel;
        set { if (!ReferenceEquals(_selectedModel, value)) { _selectedModel = value; OnPropertyChanged(); } }
    }

    private bool _isScanning;
    public bool IsScanning
    {
        get => _isScanning;
        set
        {
            if (_isScanning != value)
            {
                _isScanning = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(NotScanning));
            }
        }
    }
    public bool NotScanning => !_isScanning;

    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        set { if (_statusText != value) { _statusText = value; OnPropertyChanged(); } }
    }

    public string? ConfirmedPath { get; private set; }

    public async Task RescanAsync()
    {
        _scanCts?.Cancel();
        var cts = new CancellationTokenSource();
        _scanCts = cts;

        if (string.IsNullOrWhiteSpace(FolderPath) || !Directory.Exists(FolderPath))
        {
            _all.Clear();
            ApplyFilter();
            StatusText = BuildStatus();
            return;
        }

        IsScanning = true;
        StatusText = Localized.ModelPickerScanning;
        try
        {
            var list = await ModelScanService.ScanAsync(FolderPath, Recursive, _budget, cts.Token);
            if (cts.Token.IsCancellationRequested) return;
            _all.Clear();
            _all.AddRange(list);
            ApplyFilter();
            StatusText = BuildStatus();
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            _all.Clear();
            ApplyFilter();
            StatusText = BuildStatus();
        }
        finally
        {
            if (ReferenceEquals(_scanCts, cts)) IsScanning = false;
        }
    }

    private string BuildStatus()
    {
        var text = _all.Count == 0
            ? Localized.ModelPickerEmpty
            : _all.Count + " " + Localized.ModelPickerModels;

        if (_budget is { AvailableBytes: > 0, TotalBytes: > 0 } budget)
            text += "    " + string.Format(CultureInfo.InvariantCulture, Localized.ModelPickerVram,
                VramPlan.Gigabytes(budget.AvailableBytes), VramPlan.Gigabytes(budget.TotalBytes));

        return text;
    }

    public async Task BrowseFolderAsync()
    {
        var result = await WindowsFileDialogs.OpenFolderDialogAsync(Localized.ModelPickerTitle);
        if (!string.IsNullOrEmpty(result))
        {
            FolderPath = result;
            await RescanAsync();
        }
    }

    public bool Confirm()
    {
        if (SelectedModel == null) return false;
        ConfirmedPath = SelectedModel.FullPath;
        RequestClose?.Invoke();
        return true;
    }

    public void Cancel() => RequestClose?.Invoke();

    private void ApplyFilter()
    {
        Models.Clear();
        var filter = _filterText.Trim();
        IEnumerable<ModelScanEntry> filtered = _all;
        if (!string.IsNullOrEmpty(filter))
        {
            filtered = _all.Where(m =>
                m.FileName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                m.RelativeDir.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                m.MetaText.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }
        foreach (var m in filtered) Models.Add(m);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
