using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using LlamaServerLauncher.Models;
using LlamaServerLauncher.Resources;
using LlamaServerLauncher.Services;

namespace LlamaServerLauncher.ViewModels;

public class DownloadDialogViewModel : INotifyPropertyChanged
{
    private readonly LlamaCppDownloadService _downloadService;
    private readonly Dictionary<string, string> _bodyCache;
    private readonly List<string> _bodyCacheOrder;
    private CancellationTokenSource? _cts;
    private Timer? _debounceTimer;
    private readonly List<ReleaseInfo> _releaseCache;
    private DateTime _releaseCacheTimestamp;
    private readonly ExperimentalRepoService _experimentalRepoService = new();

    private const int MaxCachedDescriptions = 20;

    public LocalizedStrings Localized => LocalizedStrings.Instance;

    public ObservableCollection<ReleaseInfo> Releases { get; } = new();
    public ObservableCollection<ReleaseAsset> AvailableAssets { get; } = new();

    public ObservableCollection<ExperimentalRepoInfo> ExperimentalRepos { get; } = new();
    public ObservableCollection<ReleaseInfo> ExperimentalReleases { get; } = new();
    public ObservableCollection<ReleaseAsset> ExperimentalAssets { get; } = new();

    private bool _experimentalReposEnabled;
    public bool ExperimentalReposEnabled
    {
        get => _experimentalReposEnabled;
        set { _experimentalReposEnabled = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasExperimentalRepos)); }
    }

    public bool HasExperimentalRepos => _experimentalReposEnabled && ExperimentalRepos.Count > 0;

    // 0 = Official, 1 = Experimental. Loading experimental releases is deferred until
    // the user actually selects the Experimental tab.
    private int _selectedTabIndex;
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (_selectedTabIndex != value)
            {
                _selectedTabIndex = value;
                OnPropertyChanged();
                if (value == 1)
                    EnsureExperimentalLoaded();
            }
        }
    }

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
                UpdateReleaseDescription();
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

    private ExperimentalRepoInfo? _selectedExperimentalRepo;
    public ExperimentalRepoInfo? SelectedExperimentalRepo
    {
        get => _selectedExperimentalRepo;
        set
        {
            if (_selectedExperimentalRepo != value)
            {
                _selectedExperimentalRepo = value;
                OnPropertyChanged();
                _ = LoadExperimentalReleasesAsync();
            }
        }
    }

    private ReleaseInfo? _selectedExperimentalRelease;
    public ReleaseInfo? SelectedExperimentalRelease
    {
        get => _selectedExperimentalRelease;
        set
        {
            if (_selectedExperimentalRelease != value)
            {
                _selectedExperimentalRelease = value;
                OnPropertyChanged();
                PopulateExperimentalAssets();
                OnPropertyChanged(nameof(CanDownloadExperimental));
            }
        }
    }

    private ReleaseAsset? _selectedExperimentalAsset;
    public ReleaseAsset? SelectedExperimentalAsset
    {
        get => _selectedExperimentalAsset;
        set
        {
            if (_selectedExperimentalAsset != value)
            {
                _selectedExperimentalAsset = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanDownloadExperimental));
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
        set { _isDownloading = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanDownload)); OnPropertyChanged(nameof(CanDownloadExperimental)); OnPropertyChanged(nameof(ShowProgress)); }
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

    private string _releaseDescription = "";
    public string ReleaseDescription
    {
        get => _releaseDescription;
        set { _releaseDescription = value; OnPropertyChanged(); }
    }

    public bool CanDownload
    {
        get
        {
            if (IsDownloading || IsLoading) return false;
            return SelectedAsset != null && !IsReleaseNotFound;
        }
    }

    public bool CanDownloadExperimental
    {
        get
        {
            if (IsDownloading) return false;
            return SelectedExperimentalAsset != null;
        }
    }

    public bool ShowProgress => IsDownloading;

    public string? DownloadedReleaseTag { get; private set; }
    public string? DownloadedExecutablePath { get; private set; }
    public string? LastCustomDownloadPath { get; set; }
    public bool DownloadSucceeded { get; private set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? RequestClose;

    public DownloadDialogViewModel(LlamaCppDownloadService downloadService, Dictionary<string, string> bodyCache, List<string> bodyCacheOrder, List<ReleaseInfo> releaseCache, DateTime releaseCacheTimestamp, string? preselectedTag = null)
    {
        _downloadService = downloadService;
        _bodyCache = bodyCache;
        _bodyCacheOrder = bodyCacheOrder;
        _releaseCache = releaseCache;
        _releaseCacheTimestamp = releaseCacheTimestamp;
        _ = LoadReleasesAsync(preselectedTag);
    }

    public void SetExperimentalRepos(bool enabled, ObservableCollection<ExperimentalRepoInfo> repos)
    {
        ExperimentalReposEnabled = enabled;
        ExperimentalRepos.Clear();
        if (enabled)
        {
            foreach (var repo in repos.Where(r => r.Enabled))
                ExperimentalRepos.Add(repo);
        }
        OnPropertyChanged(nameof(HasExperimentalRepos));
    }

    public void EnsureExperimentalLoaded()
    {
        if (_selectedExperimentalRepo == null && ExperimentalRepos.Count > 0)
            SelectedExperimentalRepo = ExperimentalRepos[0];
    }

    private async Task LoadExperimentalReleasesAsync()
    {
        ExperimentalReleases.Clear();
        ExperimentalAssets.Clear();
        SelectedExperimentalRelease = null;
        SelectedExperimentalAsset = null;

        if (_selectedExperimentalRepo == null) return;

        List<ReleaseInfo> releases;
        if (_selectedExperimentalRepo.CachedReleases.Count > 0
            && (DateTime.Now - _selectedExperimentalRepo.CachedReleasesTimestamp) < TimeSpan.FromMinutes(30))
        {
            releases = _selectedExperimentalRepo.CachedReleases;
        }
        else
        {
            try
            {
                releases = await _experimentalRepoService.FetchReleasesAsync(_selectedExperimentalRepo);
                _selectedExperimentalRepo.CachedReleases = releases;
                _selectedExperimentalRepo.CachedReleasesTimestamp = DateTime.Now;
            }
            catch
            {
                // Fetch failed (e.g. GitHub rate limit); fall back to the stale cache if any.
                releases = _selectedExperimentalRepo.CachedReleases;
            }
        }

        foreach (var r in releases)
            ExperimentalReleases.Add(r);

        if (ExperimentalReleases.Count > 0)
            SelectedExperimentalRelease = ExperimentalReleases[0];
    }

    private void PopulateExperimentalAssets()
    {
        ExperimentalAssets.Clear();
        SelectedExperimentalAsset = null;

        if (_selectedExperimentalRelease == null || _selectedExperimentalRepo == null) return;

        var filtered = ExperimentalRepoService.FilterAssetsByTags(
            _selectedExperimentalRelease.Assets, _selectedExperimentalRepo.FilterTags);
        foreach (var a in filtered)
            ExperimentalAssets.Add(a);

        if (ExperimentalAssets.Count > 0)
            SelectedExperimentalAsset = ExperimentalAssets[0];
    }

    private async Task LoadReleasesAsync(string? preselectedTag = null)
    {
        IsLoading = true;
        StatusMessage = LocalizedStrings.GetString("LoadingReleases");
        IsReleaseNotFound = false;

        if (_releaseCache.Count > 0 && (DateTime.Now - _releaseCacheTimestamp) < TimeSpan.FromMinutes(30))
        {
            Dispatcher.UIThread.Post(() =>
            {
                PopulateReleasesList(_releaseCache, preselectedTag);
                IsLoading = false;
                StatusMessage = "";
            });
            return;
        }

        try
        {
            var releases = await _downloadService.GetLatestReleasesAsync(10);

            _releaseCache.Clear();
            foreach (var r in releases)
                _releaseCache.Add(r);
            _releaseCacheTimestamp = DateTime.Now;

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
                // Fall back to the cached releases (even if stale) instead of showing an
                // empty list — typically when GitHub rate-limits the request (403).
                if (_releaseCache.Count > 0)
                    PopulateReleasesList(_releaseCache, preselectedTag);
                StatusMessage = string.Format(LocalizedStrings.GetString("DownloadFailed"), ex.Message);
            });
        }
    }

    private void PopulateReleasesList(IReadOnlyList<ReleaseInfo> releases, string? preselectedTag)
    {
        Releases.Clear();
        foreach (var r in releases)
            Releases.Add(r);

        if (!string.IsNullOrEmpty(preselectedTag))
        {
            var match = Releases.FirstOrDefault(r => r.Tag == preselectedTag)
                ?? (Releases.Count > 0 ? Releases[0] : null);
            if (match != null)
                SelectedRelease = match;
        }
        else if (Releases.Count > 0)
        {
            SelectedRelease = Releases[0];
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

    private void UpdateReleaseDescription()
    {
        if (_selectedRelease == null || string.IsNullOrWhiteSpace(_selectedRelease.Body))
        {
            ReleaseDescription = "";
            return;
        }

        CacheReleaseBody(_selectedRelease.Tag, _selectedRelease.Body);
        ReleaseDescription = CleanReleaseBody(_selectedRelease.Body);
    }

    private static string CleanReleaseBody(string body)
    {
        var text = body;

        text = Regex.Replace(text, @"<details\s+open>\s*", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</details>", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<details[^>]*>.*?</details>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        text = Regex.Replace(text, @"\[([^\]]*)\]\([^)]*\)", "$1");
        text = Regex.Replace(text, @"\*\*([^*]*)\*\*", "$1");
        text = Regex.Replace(text, @"^\s*[\r\n]", "", RegexOptions.Multiline);
        text = text.Trim();

        return text;
    }

    private void CacheReleaseBody(string tag, string body)
    {
        if (string.IsNullOrEmpty(tag) || string.IsNullOrEmpty(body)) return;

        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(body));

        if (_bodyCache.ContainsKey(tag))
        {
            _bodyCache[tag] = encoded;
            _bodyCacheOrder.Remove(tag);
            _bodyCacheOrder.Add(tag);
        }
        else
        {
            _bodyCache[tag] = encoded;
            _bodyCacheOrder.Add(tag);
        }

        while (_bodyCacheOrder.Count > MaxCachedDescriptions)
        {
            var oldest = _bodyCacheOrder[0];
            _bodyCacheOrder.RemoveAt(0);
            _bodyCache.Remove(oldest);
        }
    }

    public string? GetCachedReleaseBody(string tag)
    {
        if (string.IsNullOrEmpty(tag)) return null;
        if (!_bodyCache.TryGetValue(tag, out var encoded)) return null;
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        }
        catch
        {
            return null;
        }
    }

    private void RestartDebounce()
    {
        _debounceTimer?.Dispose();
        _debounceTimer = null;

        if (string.IsNullOrWhiteSpace(_manualTagInput))
            return;

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

        await ExecuteDownloadAsync(_downloadService.InstallDirectory, promptForPath: true);
    }

    public async Task DownloadToFolderAsync()
    {
        if (SelectedAsset == null || IsDownloading) return;

        var folder = await WindowsFileDialogs.OpenFolderDialogAsync(LocalizedStrings.GetString("SelectDownloadFolder"));
        if (string.IsNullOrEmpty(folder)) return;

        var tag = _selectedRelease?.Tag ?? "llama.cpp";
        var targetDirectory = LlamaCppDownloadService.GetUniqueSubfolderPath(folder, tag);

        await ExecuteDownloadAsync(targetDirectory, promptForPath: false);

        if (DownloadSucceeded)
        {
            LastCustomDownloadPath = folder;
            DownloadedExecutablePath = _downloadService.GetLlamaServerPath(targetDirectory);
        }
    }

    public async Task DownloadExperimentalAsync()
    {
        if (SelectedExperimentalAsset == null || IsDownloading || _selectedExperimentalRepo == null) return;

        var folder = await WindowsFileDialogs.OpenFolderDialogAsync(LocalizedStrings.GetString("SelectDownloadFolder"));
        if (string.IsNullOrEmpty(folder)) return;

        var repoName = !string.IsNullOrEmpty(_selectedExperimentalRepo.DisplayName)
            ? _selectedExperimentalRepo.DisplayName
            : "experimental";
        var tag = _selectedExperimentalRelease?.Tag ?? repoName;
        var subFolder = Path.Combine(folder, "ExperimentalRepos", repoName);
        var targetDirectory = LlamaCppDownloadService.GetUniqueSubfolderPath(subFolder, tag);

        await ExecuteDownloadAsync(targetDirectory, promptForPath: false, asset: SelectedExperimentalAsset, allAssets: _selectedExperimentalRelease?.Assets);

        if (DownloadSucceeded)
        {
            DownloadedExecutablePath = _downloadService.GetLlamaServerPath(targetDirectory);
            LastCustomDownloadPath = folder;
        }
    }

    private async Task ExecuteDownloadAsync(string targetDirectory, bool promptForPath, ReleaseAsset? asset = null, List<ReleaseAsset>? allAssets = null)
    {
        asset ??= SelectedAsset;
        allAssets ??= _selectedRelease?.Assets;
        if (asset == null) return;

        IsDownloading = true;
        StatusMessage = LocalizedStrings.GetString("Downloading");
        _cts = new CancellationTokenSource();
        DownloadSucceeded = false;

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
            await _downloadService.DownloadAndExtractAsync(asset, targetDirectory, progress, _cts.Token,
                _downloadService.FindMatchingCudaDllAsset(asset, allAssets));

            if (promptForPath && !_downloadService.IsInPath(targetDirectory))
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
                    await _downloadService.AddToPathIfNeededAsync(targetDirectory);
                }
            }

            DownloadedReleaseTag = _selectedRelease?.Tag;
            DownloadSucceeded = true;

            Dispatcher.UIThread.Post(() =>
            {
                StatusMessage = LocalizedStrings.GetString("DownloadComplete");
                IsDownloading = false;
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
