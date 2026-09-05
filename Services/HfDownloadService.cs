using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LlamaServerLauncher.Models;

namespace LlamaServerLauncher.Services;

public enum HfDownloadPhase
{
    Connecting,
    Downloading,
    Restarting,
    Verifying,
    Finalizing,
    Done,
}

public sealed record HfDownloadProgress
{
    public long BytesDone { get; init; }
    public long BytesTotal { get; init; }
    public double BytesPerSecond { get; init; }
    public TimeSpan? Eta { get; init; }
    public int FileIndex { get; init; }
    public int FileCount { get; init; }
    public string CurrentFileName { get; init; } = "";
    public HfDownloadPhase Phase { get; init; }

    public double Percent => BytesTotal > 0 ? Math.Clamp(BytesDone * 100.0 / BytesTotal, 0, 100) : 0;
}

public sealed record HfDownloadOutcome(
    bool Success,
    string? PrimaryPath,
    HfError? Error,
    bool Cancelled,
    bool Resumable,
    long BytesWritten);

public sealed class HfDownloadRequest
{
    public HfRepoRef Repo { get; init; } = new();
    public IReadOnlyList<HfRemoteFile> Files { get; init; } = Array.Empty<HfRemoteFile>();
    public string TargetDirectory { get; init; } = "";
    public bool UseRepoSubfolder { get; init; }
}

public sealed class HfDownloadService
{
    private const int MaxRedirects = 5;
    private const int MaxAttempts = 5;
    private const int BufferBytes = 1 << 20;
    private const int ProgressIntervalMs = 250;
    private const long ProgressBytesStep = 8L << 20;
    private const double BackoffCapSeconds = 30.0;

    private static readonly int[] RetryStatuses = { 408, 425, 429, 500, 502, 503, 504 };

    private static readonly HttpClient SharedFiles =
        new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };

    private readonly HttpClient _http;
    private readonly HfTokenProvider _tokens;
    private readonly Action<string>? _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<int, Task> _delay;

    public HfDownloadService(HfTokenProvider? tokens = null, Action<string>? log = null)
    {
        _tokens = tokens ?? new HfTokenProvider();
        _log = log;
        _http = SharedFiles;
        _delay = ms => Task.Delay(ms);
    }

    internal HfDownloadService(HttpMessageHandler handler, HfTokenProvider? tokens = null,
        Action<string>? log = null, Func<int, Task>? delay = null)
    {
        _tokens = tokens ?? new HfTokenProvider();
        _log = log;
        _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _delay = delay ?? (ms => Task.Delay(ms));
    }

    public bool IsBusy => _gate.CurrentCount == 0;

    public async Task<HfDownloadOutcome> DownloadAsync(HfDownloadRequest request,
        IProgress<HfDownloadProgress>? progress, CancellationToken ct)
    {
        if (request.Files.Count == 0)
            return new HfDownloadOutcome(false, null, new HfError(HfErrorKind.NotFound, 0, "no files"),
                false, false, 0);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await RunAsync(request, progress, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<HfDownloadOutcome> RunAsync(HfDownloadRequest request,
        IProgress<HfDownloadProgress>? progress, CancellationToken ct)
    {
        var directory = request.TargetDirectory;
        bool shardSet = request.Files.Count > 1;
        if (shardSet || request.UseRepoSubfolder)
            directory = Path.Combine(directory, HfDownloadPlan.RepoFolderName(request.Repo.RepoId));

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex)
        {
            return Failed(new HfError(HfErrorKind.Network, 0, ex.Message));
        }

        long remaining = 0;
        foreach (var file in request.Files)
        {
            var target = Path.Combine(directory, file.FileName);
            long have = OnDisk(target, file.SizeBytes);
            remaining += Math.Max(0, file.SizeBytes - have);
        }

        if (!HfDownloadPlan.HasEnoughFreeSpace(directory, remaining, out long available))
            return Failed(new HfError(HfErrorKind.NoSpace, 0,
                remaining.ToString(CultureInfo.InvariantCulture) + "/"
                + available.ToString(CultureInfo.InvariantCulture)));

        long total = 0;
        foreach (var file in request.Files) total += file.SizeBytes;

        long completed = 0;
        string? primary = null;
        var reporter = new ProgressReporter(progress, total, request.Files.Count);

        for (int index = 0; index < request.Files.Count; index++)
        {
            var file = request.Files[index];
            if (!HfDownloadPlan.TrySafeDestination(directory, file.FileName, out var target))
                return Failed(new HfError(HfErrorKind.Network, 0, file.FileName), completed);

            if (HfDownloadPlan.IsPathTooLong(target))
                return Failed(new HfError(HfErrorKind.Network, 0, "path too long: " + target), completed);

            reporter.BeginFile(index, file.FileName, completed);

            var outcome = await FetchFileAsync(request.Repo, file, target, reporter, ct).ConfigureAwait(false);
            if (!outcome.Success)
                return outcome with { BytesWritten = completed + outcome.BytesWritten };

            primary ??= target;
            completed += file.SizeBytes;
        }

        reporter.Report(completed, HfDownloadPhase.Done);
        return new HfDownloadOutcome(true, primary, null, false, false, completed);
    }

    private async Task<HfDownloadOutcome> FetchFileAsync(HfRepoRef repo, HfRemoteFile file, string target,
        ProgressReporter reporter, CancellationToken ct)
    {
        var url = repo.ResolveUrl(file.Path);
        var partPath = HfDownloadPlan.PartPathFor(target);
        var statePath = HfDownloadPlan.StatePathFor(target);

        if (File.Exists(target) && FileLength(target) == file.SizeBytes && file.SizeBytes > 0)
            return new HfDownloadOutcome(true, target, null, false, false, file.SizeBytes);

        var state = ReadState(statePath);
        var decision = HfDownloadPlan.DecideResume(FileLength(partPath), state, file.SizeBytes, file.Oid,
            out long offset);

        if (decision == HfResumeDecision.Conflict)
        {
            TryDelete(partPath);
            TryDelete(statePath);
            offset = 0;
            decision = HfResumeDecision.StartFresh;
        }

        if (decision == HfResumeDecision.AlreadyComplete)
        {
            if (TryPromote(partPath, target, out var promoteError))
            {
                TryDelete(statePath);
                return new HfDownloadOutcome(true, target, null, false, false, file.SizeBytes);
            }
            return Failed(promoteError!, FileLength(partPath));
        }

        WriteState(statePath, new HfPartState
        {
            Url = url,
            RepoId = repo.RepoId,
            Revision = repo.Revision,
            Path = file.Path,
            ExpectedSize = file.SizeBytes,
            Oid = file.Oid,
            StartedUtc = DateTime.UtcNow,
        });

        HfError? lastError = null;
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            if (ct.IsCancellationRequested)
                return new HfDownloadOutcome(false, null, null, true, true, FileLength(partPath));

            long have = FileLength(partPath);
            if (file.SizeBytes > 0 && have >= file.SizeBytes) have = 0;

            var attemptResult = await AttemptAsync(url, partPath, have, file, reporter, ct)
                .ConfigureAwait(false);

            if (attemptResult.Cancelled)
                return new HfDownloadOutcome(false, null, null, true, true, FileLength(partPath));

            if (attemptResult.Error == null)
            {
                long written = FileLength(partPath);
                if (file.SizeBytes > 0 && written != file.SizeBytes)
                {
                    lastError = new HfError(HfErrorKind.Verify, 0,
                        written.ToString(CultureInfo.InvariantCulture) + "/"
                        + file.SizeBytes.ToString(CultureInfo.InvariantCulture));
                    break;
                }

                reporter.SetPhase(HfDownloadPhase.Finalizing);
                if (!TryPromote(partPath, target, out var promoteError))
                    return Failed(promoteError!, written);

                TryDelete(statePath);
                return new HfDownloadOutcome(true, target, null, false, false, written);
            }

            lastError = attemptResult.Error;
            if (!attemptResult.Retryable || attempt == MaxAttempts) break;

            _log?.Invoke("hf: retrying " + file.FileName + " after " + attemptResult.Error.Kind);
            await _delay((int)(Math.Min(Math.Pow(2, attempt), BackoffCapSeconds) * 1000)).ConfigureAwait(false);
        }

        return Failed(lastError ?? new HfError(HfErrorKind.Network, 0, file.FileName), FileLength(partPath));
    }

    private async Task<AttemptResult> AttemptAsync(string url, string partPath, long have, HfRemoteFile file,
        ProgressReporter reporter, CancellationToken ct)
    {
        HttpResponseMessage? response = null;
        try
        {
            reporter.SetPhase(have > 0 ? HfDownloadPhase.Downloading : HfDownloadPhase.Connecting);

            var resolved = await WalkAsync(url, have, ct).ConfigureAwait(false);
            if (resolved.Error != null) return new AttemptResult(resolved.Error, resolved.Retryable, false);
            response = resolved.Response!;

            int status = (int)response.StatusCode;
            string? contentRange = Header(response, "Content-Range");

            if (have > 0 && !HfDownloadPlan.RangeHonored(status, contentRange, have))
            {
                _log?.Invoke("hf: server ignored the resume request for " + file.FileName + ", starting over");
                reporter.SetPhase(HfDownloadPhase.Restarting);
                have = 0;
            }

            long? total = HfDownloadPlan.TotalFromResponse(contentRange,
                response.Content.Headers.ContentLength, have);
            reporter.SetFileTotal(file.SizeBytes > 0 ? file.SizeBytes : total ?? 0);

            var mode = have > 0 ? FileMode.Open : FileMode.Create;
            using var output = new FileStream(partPath, mode, FileAccess.Write, FileShare.None,
                BufferBytes, FileOptions.Asynchronous);
            if (have > 0)
            {
                output.Seek(have, SeekOrigin.Begin);
                output.SetLength(have);
            }

            using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var buffer = new byte[BufferBytes];
            long done = have;
            reporter.ReportFile(done, HfDownloadPhase.Downloading, force: true);

            while (true)
            {
                int read = await input.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
                if (read <= 0) break;
                await output.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                done += read;
                reporter.ReportFile(done, HfDownloadPhase.Downloading, force: false);
            }

            await output.FlushAsync(ct).ConfigureAwait(false);
            reporter.ReportFile(done, HfDownloadPhase.Downloading, force: true);
            return new AttemptResult(null, false, false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new AttemptResult(null, false, true);
        }
        catch (Exception ex)
        {
            return new AttemptResult(new HfError(HfErrorKind.Network, 0, ex.Message), true, false);
        }
        finally
        {
            response?.Dispose();
        }
    }

    private async Task<Resolved> WalkAsync(string url, long offset, CancellationToken ct)
    {
        var origin = new Uri(url);
        var current = origin;
        var token = _tokens.Resolve();

        for (int hop = 0; hop <= MaxRedirects; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.UserAgent.ParseAdd(HuggingFaceClient.UserAgent);
            request.Headers.AcceptEncoding.ParseAdd("identity");
            if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
            if (!string.IsNullOrEmpty(token) && HfDownloadPlan.ShouldForwardAuth(origin, current))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            int status = (int)response.StatusCode;
            if (status is >= 300 and < 400 && response.Headers.Location != null)
            {
                current = new Uri(current, response.Headers.Location);
                response.Dispose();
                continue;
            }

            if (status >= 400)
            {
                response.Dispose();
                bool retryable = Array.IndexOf(RetryStatuses, status) >= 0;
                return new Resolved(null, HfError.From(status, url), retryable);
            }

            return new Resolved(response, null, false);
        }

        return new Resolved(null, new HfError(HfErrorKind.Network, 0, "too many redirects"), false);
    }

    private static string? Header(HttpResponseMessage response, string name)
    {
        if (response.Content.Headers.TryGetValues(name, out var fromContent))
            return string.Join(",", fromContent);
        if (response.Headers.TryGetValues(name, out var fromMessage))
            return string.Join(",", fromMessage);
        return null;
    }

    private static long OnDisk(string target, long expectedSize)
    {
        long final = FileLength(target);
        if (expectedSize > 0 && final == expectedSize) return final;
        return FileLength(HfDownloadPlan.PartPathFor(target));
    }

    private static long FileLength(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static bool TryPromote(string partPath, string target, out HfError? error)
    {
        error = null;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                File.Move(partPath, target, overwrite: true);
                return true;
            }
            catch (Exception ex)
            {
                error = new HfError(HfErrorKind.Network, 0, ex.Message);
                Thread.Sleep(300);
            }
        }
        return false;
    }

    private static HfPartState? ReadState(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<HfPartState>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    private static void WriteState(string path, HfPartState state)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(state,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
        }
    }

    private static HfDownloadOutcome Failed(HfError error, long written = 0) =>
        new(false, null, error, false, error.Kind is HfErrorKind.Network or HfErrorKind.Server, written);

    private sealed record Resolved(HttpResponseMessage? Response, HfError? Error, bool Retryable);

    private sealed record AttemptResult(HfError? Error, bool Retryable, bool Cancelled);

    private sealed class ProgressReporter
    {
        private readonly IProgress<HfDownloadProgress>? _sink;
        private readonly long _totalBytes;
        private readonly int _fileCount;

        private int _index;
        private string _name = "";
        private long _baseBytes;
        private long _fileTotal;
        private long _lastReported;
        private long _lastTicks;
        private double _speed;
        private long _speedMarkBytes;
        private long _speedMarkTicks;
        private HfDownloadPhase _phase = HfDownloadPhase.Connecting;

        public ProgressReporter(IProgress<HfDownloadProgress>? sink, long totalBytes, int fileCount)
        {
            _sink = sink;
            _totalBytes = totalBytes;
            _fileCount = fileCount;
        }

        public void BeginFile(int index, string name, long baseBytes)
        {
            _index = index;
            _name = name;
            _baseBytes = baseBytes;
            _lastReported = 0;
            _lastTicks = Environment.TickCount64;
            _speedMarkTicks = _lastTicks;
            _speedMarkBytes = 0;
            _speed = 0;
        }

        public void SetPhase(HfDownloadPhase phase) => _phase = phase;

        public void SetFileTotal(long total) => _fileTotal = total;

        public void ReportFile(long fileBytes, HfDownloadPhase phase, bool force)
        {
            long now = Environment.TickCount64;
            if (!force && now - _lastTicks < ProgressIntervalMs
                && fileBytes - _lastReported < ProgressBytesStep)
                return;

            double elapsed = (now - _speedMarkTicks) / 1000.0;
            if (elapsed >= 0.35)
            {
                double instant = Math.Max(0, fileBytes - _speedMarkBytes) / elapsed;
                _speed = _speed <= 0 ? instant : _speed * 0.75 + instant * 0.25;
                _speedMarkTicks = now;
                _speedMarkBytes = fileBytes;
            }

            _lastTicks = now;
            _lastReported = fileBytes;
            _phase = phase;
            Report(_baseBytes + fileBytes, phase);
        }

        public void Report(long doneBytes, HfDownloadPhase phase)
        {
            if (_sink == null) return;
            long left = Math.Max(0, _totalBytes - doneBytes);
            _sink.Report(new HfDownloadProgress
            {
                BytesDone = doneBytes,
                BytesTotal = _totalBytes > 0 ? _totalBytes : _fileTotal,
                BytesPerSecond = _speed,
                Eta = _speed > 0 && left > 0 ? TimeSpan.FromSeconds(left / _speed) : null,
                FileIndex = _index,
                FileCount = _fileCount,
                CurrentFileName = _name,
                Phase = phase,
            });
        }
    }
}
