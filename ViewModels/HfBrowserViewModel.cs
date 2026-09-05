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

public sealed class HfBrowserOptions
{
    public string Endpoint { get; set; } = "";
    public string TargetFolder { get; set; } = "";
    public string LastQuery { get; set; } = "";
    public string Token { get; set; } = "";
    public bool SubfolderPerRepo { get; set; }
}

public class HfBrowserViewModel : INotifyPropertyChanged
{
    public LocalizedStrings Localized { get; } = LocalizedStrings.Instance;

    private readonly HfTokenProvider _tokens;
    private readonly HuggingFaceClient _client;
    private readonly HfDownloadService _downloads;
    private readonly VramBudget? _budget;
    private readonly string _endpoint;

    private CancellationTokenSource? _browseCts;
    private CancellationTokenSource? _downloadCts;

    public ObservableCollection<HfRepoSummary> Repos { get; } = new();
    public ObservableCollection<HfQuantEntry> Quants { get; } = new();

    public event Action? RequestClose;

    public HfBrowserViewModel(HfBrowserOptions options, VramBudget? budget = null, Action<string>? log = null)
    {
        var configured = string.IsNullOrWhiteSpace(options.Endpoint)
            ? HfRepoRef.DefaultEndpointFromEnvironment()
            : options.Endpoint;
        _endpoint = HfRepoRef.NormaliseEndpoint(configured);
        _queryText = options.LastQuery ?? "";
        _targetFolder = options.TargetFolder ?? "";
        _subfolderPerRepo = options.SubfolderPerRepo;
        _token = options.Token ?? "";
        _budget = budget;
        _tokens = new HfTokenProvider(() => _token);
        _client = new HuggingFaceClient(_tokens);
        _downloads = new HfDownloadService(_tokens, log);
        _statusText = BuildIdleStatus();
    }

    public string Title => Localized.HfBrowserTitle;

    public string Endpoint => _endpoint;

    private string _queryText = "";
    public string QueryText
    {
        get => _queryText;
        set { if (_queryText != value) { _queryText = value; OnPropertyChanged(); } }
    }

    private string _targetFolder = "";
    public string TargetFolder
    {
        get => _targetFolder;
        set
        {
            if (_targetFolder == value) return;
            _targetFolder = value;
            OnPropertyChanged();
            RefreshLocalState();
        }
    }

    private bool _subfolderPerRepo;
    public bool SubfolderPerRepo
    {
        get => _subfolderPerRepo;
        set
        {
            if (_subfolderPerRepo == value) return;
            _subfolderPerRepo = value;
            OnPropertyChanged();
            RefreshLocalState();
        }
    }

    private string _token = "";
    public string Token
    {
        get => _token;
        set
        {
            if (_token == value) return;
            _token = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasToken));
        }
    }

    public bool HasToken => _tokens.HasToken;

    private HfRepoSummary? _selectedRepo;
    public HfRepoSummary? SelectedRepo
    {
        get => _selectedRepo;
        set
        {
            if (ReferenceEquals(_selectedRepo, value)) return;
            _selectedRepo = value;
            OnPropertyChanged();
            if (value != null) _ = OpenRepoAsync(value.Id);
        }
    }

    private HfQuantEntry? _selectedQuant;
    public HfQuantEntry? SelectedQuant
    {
        get => _selectedQuant;
        set
        {
            if (ReferenceEquals(_selectedQuant, value)) return;
            _selectedQuant = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanDownload));
        }
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value) return;
            _isBusy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NotBusy));
            OnPropertyChanged(nameof(CanDownload));
        }
    }
    public bool NotBusy => !_isBusy;

    private bool _isDownloading;
    public bool IsDownloading
    {
        get => _isDownloading;
        private set
        {
            if (_isDownloading == value) return;
            _isDownloading = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NotDownloading));
            OnPropertyChanged(nameof(CanDownload));
        }
    }
    public bool NotDownloading => !_isDownloading;

    public bool CanDownload => !_isDownloading && !_isBusy && _selectedQuant != null;

    private double _progressPercent;
    public double ProgressPercent
    {
        get => _progressPercent;
        private set
        {
            if (Math.Abs(_progressPercent - value) <= 0.01) return;
            _progressPercent = value;
            OnPropertyChanged();
        }
    }

    private string _progressText = "";
    public string ProgressText
    {
        get => _progressText;
        private set
        {
            if (_progressText == value) return;
            _progressText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasProgress));
        }
    }
    public bool HasProgress => _progressText.Length > 0;

    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        private set { if (_statusText != value) { _statusText = value; OnPropertyChanged(); } }
    }

    private string _repoHeader = "";
    public string RepoHeader
    {
        get => _repoHeader;
        private set
        {
            if (_repoHeader == value) return;
            _repoHeader = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasRepo));
        }
    }
    public bool HasRepo => _repoHeader.Length > 0;

    private string _repoPageUrl = "";
    public string RepoPageUrl
    {
        get => _repoPageUrl;
        private set { if (_repoPageUrl != value) { _repoPageUrl = value; OnPropertyChanged(); } }
    }

    private string? _confirmedPath;
    public string? ConfirmedPath
    {
        get => _confirmedPath;
        private set
        {
            if (_confirmedPath == value) return;
            _confirmedPath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasConfirmed));
        }
    }
    public bool HasConfirmed => !string.IsNullOrEmpty(_confirmedPath);

    public async Task GoAsync()
    {
        var text = (QueryText ?? "").Trim();
        if (text.Length == 0)
        {
            StatusText = BuildIdleStatus();
            return;
        }

        if (!HfRepoRef.LooksLikeRef(text))
        {
            await SearchAsync(text).ConfigureAwait(false);
            return;
        }

        if (!HfRepoRef.TryParse(text, _endpoint, out var repo, out var error))
        {
            StatusText = DescribeRefError(error);
            return;
        }

        await OpenRepoAsync(repo.RepoId, repo.Revision).ConfigureAwait(false);
    }

    public async Task SearchAsync(string query)
    {
        var ct = BeginBrowse();
        IsBusy = true;
        StatusText = Localized.HfSearching;
        try
        {
            var result = await _client.SearchModelsAsync(query, _endpoint,
                HuggingFaceClient.DefaultSearchLimit, ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested) return;

            if (!result.Ok)
            {
                StatusText = Describe(result.Error!);
                return;
            }

            _selectedRepo = null;
            Repos.Clear();
            foreach (var repo in result.Value!) Repos.Add(repo);
            OnPropertyChanged(nameof(SelectedRepo));

            StatusText = Repos.Count == 0
                ? Localized.HfNoResults
                : string.Format(CultureInfo.InvariantCulture, Localized.HfFoundRepos, Repos.Count);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (!ct.IsCancellationRequested) IsBusy = false;
        }
    }

    public async Task OpenRepoAsync(string repoId, string revision = "main")
    {
        var ct = BeginBrowse();
        IsBusy = true;
        RepoHeader = repoId;
        RepoPageUrl = _endpoint + "/" + repoId;
        StatusText = Localized.HfLoadingFiles;
        Quants.Clear();
        SelectedQuant = null;

        try
        {
            var repo = new HfRepoRef { RepoId = repoId, Revision = revision, Endpoint = _endpoint };
            var result = await _client.ListFilesAsync(repo, ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested) return;

            if (!result.Ok)
            {
                StatusText = Describe(result.Error!);
                return;
            }

            var gguf = result.Value!
                .Where(f => f.Path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var groups = HfApiParser.GroupQuants(repoId, revision, gguf)
                .OrderBy(q => q.IsProjector ? 1 : 0)
                .ThenBy(q => q.TotalBytes)
                .ToList();

            foreach (var group in groups) Quants.Add(group);
            RefreshLocalState();

            StatusText = Quants.Count == 0
                ? Localized.HfNoGguf
                : string.Format(CultureInfo.InvariantCulture, Localized.HfFoundFiles, Quants.Count) + VramSuffix();
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (!ct.IsCancellationRequested) IsBusy = false;
        }
    }

    public async Task DownloadAsync()
    {
        if (SelectedQuant is not { } quant)
        {
            StatusText = Localized.HfPickAQuant;
            return;
        }

        if (string.IsNullOrWhiteSpace(TargetFolder))
        {
            StatusText = Localized.HfNeedFolder;
            return;
        }

        try
        {
            Directory.CreateDirectory(TargetFolder);
        }
        catch (Exception ex)
        {
            StatusText = string.Format(CultureInfo.InvariantCulture, Localized.HfErrorNetwork, ex.Message);
            return;
        }

        _downloadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _downloadCts = cts;

        IsDownloading = true;
        ProgressPercent = 0;
        ProgressText = Localized.HfConnecting;
        StatusText = string.Format(CultureInfo.InvariantCulture, Localized.HfDownloadingFrom, quant.DisplayName);

        var request = new HfDownloadRequest
        {
            Repo = new HfRepoRef { RepoId = quant.RepoId, Revision = quant.Revision, Endpoint = _endpoint },
            Files = quant.Files,
            TargetDirectory = TargetFolder,
            UseRepoSubfolder = SubfolderPerRepo || quant.IsShardSet,
        };

        var progress = new Progress<HfDownloadProgress>(OnProgress);

        try
        {
            var outcome = await _downloads.DownloadAsync(request, progress, cts.Token).ConfigureAwait(false);

            if (outcome.Cancelled)
            {
                StatusText = Localized.HfCancelled;
                ProgressText = "";
            }
            else if (outcome.Success && !string.IsNullOrEmpty(outcome.PrimaryPath))
            {
                ConfirmedPath = outcome.PrimaryPath;
                ProgressPercent = 100;
                ProgressText = "";
                StatusText = string.Format(CultureInfo.InvariantCulture, Localized.HfDone,
                    Path.GetFileName(outcome.PrimaryPath));
            }
            else
            {
                StatusText = outcome.Error != null ? Describe(outcome.Error) : Localized.HfErrorUnknown;
                ProgressText = "";
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = Localized.HfCancelled;
            ProgressText = "";
        }
        catch (Exception ex)
        {
            StatusText = string.Format(CultureInfo.InvariantCulture, Localized.HfErrorNetwork, ex.Message);
            ProgressText = "";
        }
        finally
        {
            IsDownloading = false;
            RefreshLocalState();
        }
    }

    public void CancelDownload() => _downloadCts?.Cancel();

    public async Task BrowseFolderAsync()
    {
        var result = await WindowsFileDialogs.OpenFolderDialogAsync(Localized.HfBrowserTitle);
        if (!string.IsNullOrEmpty(result)) TargetFolder = result;
    }

    public bool Confirm()
    {
        if (!HasConfirmed) return false;
        RequestClose?.Invoke();
        return true;
    }

    public void Cancel()
    {
        _browseCts?.Cancel();
        _downloadCts?.Cancel();
        RequestClose?.Invoke();
    }

    private void OnProgress(HfDownloadProgress p)
    {
        ProgressPercent = p.Percent;

        switch (p.Phase)
        {
            case HfDownloadPhase.Connecting:
                ProgressText = Localized.HfConnecting;
                return;
            case HfDownloadPhase.Restarting:
                ProgressText = Localized.HfRestarting;
                return;
            case HfDownloadPhase.Verifying:
                ProgressText = Localized.HfVerifying;
                return;
            case HfDownloadPhase.Finalizing:
                ProgressText = Localized.HfFinalizing;
                return;
        }

        var parts = new List<string>
        {
            ModelScanFormatting.FormatSize(p.BytesDone) + " / " + ModelScanFormatting.FormatSize(p.BytesTotal),
        };
        if (p.BytesPerSecond > 0) parts.Add(HfFormatting.Speed(p.BytesPerSecond));
        var eta = HfFormatting.Eta(p.Eta);
        if (eta.Length > 0) parts.Add(eta);
        if (p.FileCount > 1)
            parts.Add(string.Format(CultureInfo.InvariantCulture, Localized.HfPartOf, p.FileIndex + 1, p.FileCount));

        ProgressText = string.Join("   ", parts);
    }

    private CancellationToken BeginBrowse()
    {
        _browseCts?.Cancel();
        var cts = new CancellationTokenSource();
        _browseCts = cts;
        return cts.Token;
    }

    private void RefreshLocalState()
    {
        foreach (var quant in Quants)
        {
            var state = HfDownloadPlan.Inspect(TargetDirectoryFor(quant), quant.Files);
            quant.SetLocal(state.Complete, state.Complete ? 0 : state.HaveBytes);
        }
    }

    private string TargetDirectoryFor(HfQuantEntry quant)
    {
        if (string.IsNullOrWhiteSpace(TargetFolder)) return "";
        return SubfolderPerRepo || quant.IsShardSet
            ? Path.Combine(TargetFolder, HfDownloadPlan.RepoFolderName(quant.RepoId))
            : TargetFolder;
    }

    private string BuildIdleStatus() => Localized.HfHint + VramSuffix();

    private string VramSuffix()
    {
        if (_budget is { AvailableBytes: > 0, TotalBytes: > 0 } budget)
            return "    " + string.Format(CultureInfo.InvariantCulture, Localized.ModelPickerVram,
                VramPlan.Gigabytes(budget.AvailableBytes), VramPlan.Gigabytes(budget.TotalBytes));
        return "";
    }

    private string DescribeRefError(string error)
    {
        if (error.StartsWith("otherhost:", StringComparison.Ordinal))
            return string.Format(CultureInfo.InvariantCulture, Localized.HfErrorOtherHost,
                error.Substring("otherhost:".Length));
        if (error.StartsWith("notamodel:", StringComparison.Ordinal))
            return string.Format(CultureInfo.InvariantCulture, Localized.HfErrorNotAModel,
                error.Substring("notamodel:".Length));
        return Localized.HfErrorBadRef;
    }

    private string Describe(HfError error) => error.Kind switch
    {
        HfErrorKind.NotFound => string.Format(CultureInfo.InvariantCulture, Localized.HfErrorNotFound, error.Subject),
        HfErrorKind.Gated => Localized.HfErrorGated,
        HfErrorKind.Auth => Localized.HfErrorAuth,
        HfErrorKind.RateLimited => Localized.HfErrorRateLimited,
        HfErrorKind.NoSpace => Localized.HfErrorNoSpace,
        HfErrorKind.Verify => Localized.HfErrorVerify,
        HfErrorKind.Cancelled => Localized.HfCancelled,
        HfErrorKind.Network => string.Format(CultureInfo.InvariantCulture, Localized.HfErrorNetwork, error.Subject),
        _ => string.Format(CultureInfo.InvariantCulture, Localized.HfErrorServer,
            error.StatusCode.ToString(CultureInfo.InvariantCulture)),
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
