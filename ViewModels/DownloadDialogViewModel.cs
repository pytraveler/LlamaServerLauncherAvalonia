using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using LlamaServerLauncher.Resources;
using LlamaServerLauncher.Services;

namespace LlamaServerLauncher.ViewModels;

public class DownloadDialogViewModel : INotifyPropertyChanged
{
    private readonly LlamaCppDownloadService _downloadService;
    private CancellationTokenSource? _cts;
    private Timer? _debounceTimer;

    public LocalizedStrings Localized => LocalizedStrings.Instance;

    public ObservableCollection<ReleaseInfo> Releases { get; } = new();
    public ObservableCollection<ReleaseAsset> AvailableAssets { get; } = new();

    private ReleaseInfo? _selectedRelease;
    public ReleaseInfo? SelectedRelease
    {
        get => _selectedRelease;
        set
        {
            if (_selectedRelease != value)
            {
                _selectedRelease = value;
                OnPropertyChanged();
                PopulateAssets();
                OnPropertyChanged(nameof(CanDownload));
            }
        }
    }

    private ReleaseAsset? _selectedAsset;
    public ReleaseAsset? SelectedAsset
    {
        get => _selectedAsset;
        set
        {
            if (_selectedAsset != value)
            {
                _selectedAsset = value;
                IsReleaseNotFound = false;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanDownload));
            }
        }
    }

    private bool _isLoading = true;
    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanDownload)); }
    }

    private bool _isDownloading;
    public bool IsDownloading
    {
        get => _isDownloading;
        set { _isDownloading = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanDownload)); OnPropertyChanged(nameof(ShowProgress)); }
    }

    private double _downloadProgress;
    public double DownloadProgress
    {
        get => _downloadProgress;
        set { _downloadProgress = value; OnPropertyChanged(); }
    }

    private string _statusMessage = "";
    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    private string _manualTagInput = "";
    public string ManualTagInput
    {
        get => _manualTagInput;
        set
        {
            if (_manualTagInput != value)
            {
                _manualTagInput = value;
                OnPropertyChanged();
                RestartDebounce();
            }
        }
    }

    private bool _isReleaseNotFound;
    public bool IsReleaseNotFound
    {
        get => _isReleaseNotFound;
        set { _isReleaseNotFound = value; OnPropertyChanged(); }
    }

    public bool CanDownload => SelectedAsset != null && !IsDownloading && !IsLoading && !IsReleaseNotFound;
    public bool ShowProgress => IsDownloading;

    /// <summary>
    /// Tag of the release that was actually downloaded (null if not downloaded).
    /// </summary>
    public string? DownloadedReleaseTag { get; private set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? RequestClose;

    public DownloadDialogViewModel(LlamaCppDownloadService downloadService, string? preselectedTag = null)
    {
        _downloadService = downloadService;
        _ = LoadReleasesAsync(preselectedTag);
    }

    private async Task LoadReleasesAsync(string? preselectedTag = null)
    {
        IsLoading = true;
        StatusMessage = LocalizedStrings.GetString("LoadingReleases");
        IsReleaseNotFound = false;

        try
        {
            var releases = await _downloadService.GetLatestReleasesAsync(10);

            Dispatcher.UIThread.Post(() =>
            {
                Releases.Clear();
                foreach (var r in releases)
                    Releases.Add(r);

                if (!string.IsNullOrEmpty(preselectedTag))
                {
                    var match = Releases.Count > 0 ? Releases[0] : null;
                    if (match != null)
                        SelectedRelease = match;
                }
                else if (Releases.Count > 0)
                {
                    SelectedRelease = Releases[0];
                }

                IsLoading = false;
                StatusMessage = "";
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsLoading = false;
                StatusMessage = string.Format(LocalizedStrings.GetString("DownloadFailed"), ex.Message);
            });
        }
    }

    private void PopulateAssets()
    {
        AvailableAssets.Clear();
        SelectedAsset = null;
        IsReleaseNotFound = false;

        if (_selectedRelease == null) return;

        var filtered = _downloadService.FilterAssetsForCurrentOS(_selectedRelease.Assets);
        foreach (var a in filtered)
            AvailableAssets.Add(a);

        if (AvailableAssets.Count > 0)
            SelectedAsset = AvailableAssets[0];
        else
            StatusMessage = LocalizedStrings.GetString("NoAssetsForOS");
    }

    private void RestartDebounce()
    {
        _debounceTimer?.Dispose();
        _debounceTimer = null;

        if (string.IsNullOrWhiteSpace(_manualTagInput))
            return;

        // Text came from selecting an item in the ComboBox, not from user typing
        if (_selectedRelease != null && _manualTagInput == _selectedRelease.ToString())
            return;

        _debounceTimer = new Timer(async _ =>
        {
            try
            {
                var tag = _manualTagInput.Trim();
                var release = await _downloadService.GetReleaseByTagAsync(tag);

                Dispatcher.UIThread.Post(() =>
                {
                    if (release != null)
                    {
                        IsReleaseNotFound = false;
                        Releases.Clear();
                        Releases.Add(release);
                        SelectedRelease = release;
                        StatusMessage = "";
                    }
                    else
                    {
                        IsReleaseNotFound = true;
                        StatusMessage = LocalizedStrings.GetString("ReleaseNotFound");
                    }
                });
            }
            catch
            {
                Dispatcher.UIThread.Post(() =>
                {
                    IsReleaseNotFound = true;
                    StatusMessage = LocalizedStrings.GetString("ReleaseNotFound");
                });
            }
        }, null, TimeSpan.FromSeconds(7), Timeout.InfiniteTimeSpan);
    }

    public async Task DownloadAsync()
    {
        if (SelectedAsset == null || IsDownloading) return;

        IsDownloading = true;
        StatusMessage = LocalizedStrings.GetString("Downloading");
        _cts = new CancellationTokenSource();

        var progress = new Progress<double>(p =>
        {
            if (p < 0)
            {
                StatusMessage = LocalizedStrings.GetString("Extracting");
                DownloadProgress = 0;
            }
            else
            {
                DownloadProgress = p;
            }
        });

        try
        {
            await _downloadService.DownloadAndExtractAsync(SelectedAsset, progress, _cts.Token,
                _downloadService.FindMatchingCudaDllAsset(SelectedAsset, _selectedRelease?.Assets));

            if (!_downloadService.IsInPath(_downloadService.InstallDirectory))
            {
                var pathPrompt = LocalizedStrings.GetString("AddToPathPrompt");
                var result = await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var dlgResult = await MessageBox.ShowAsync(
                        MainWindow.Instance!,
                        pathPrompt,
                        LocalizedStrings.GetString("ConfirmTitle"),
                        MessageBoxButtons.YesNoCancel,
                        MessageBoxIcon.Question);
                    return dlgResult;
                });

                if (result == MessageBoxResult.Yes)
                {
                    await _downloadService.AddToPathIfNeededAsync(_downloadService.InstallDirectory);
                }
            }

            DownloadedReleaseTag = _selectedRelease?.Tag;

            Dispatcher.UIThread.Post(() =>
            {
                StatusMessage = LocalizedStrings.GetString("DownloadComplete");
                IsDownloading = false;
                // Auto-close after successful download
                _ = Task.Delay(800).ContinueWith(_ =>
                    Dispatcher.UIThread.Post(() => RequestClose?.Invoke()));
            });
        }
        catch (OperationCanceledException)
        {
            Dispatcher.UIThread.Post(() =>
            {
                StatusMessage = LocalizedStrings.GetString("DownloadFailed").Replace("{0}", "Cancelled");
                IsDownloading = false;
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                StatusMessage = string.Format(LocalizedStrings.GetString("DownloadFailed"), ex.Message);
                IsDownloading = false;
            });
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    public void CancelDownload()
    {
        _cts?.Cancel();
    }

    public void Close()
    {
        CancelDownload();
        _debounceTimer?.Dispose();
        RequestClose?.Invoke();
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
