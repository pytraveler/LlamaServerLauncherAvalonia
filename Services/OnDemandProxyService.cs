using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LlamaServerLauncher.Models;
using LlamaServerLauncher.ViewModels;

namespace LlamaServerLauncher.Services;

public sealed class ProxyOptions
{
    public int Port { get; init; } = 8081;
    public int IdleSeconds { get; init; } = 300;
    public string? ApiKey { get; init; }
}

public sealed class OnDemandProxyService : IDisposable
{
    private const int MaxHeaderBytes = 64 * 1024;
    private const int MaxBodyBytes = 32 * 1024 * 1024;

    private readonly LogService _logService;
    private readonly IOnDemandProxyHost _host;

    private TcpListener? _tcpListener;
    private CancellationTokenSource? _cts;
    private HttpClient? _http;
    private ProxyOptions? _options;
    private readonly SemaphoreSlim _swapLock = new(1, 1);
    private bool _disposed;
    private int _port;
    private long _inflight;
    private long _lastUseTicks;

    public bool IsRunning => _tcpListener != null;
    public int Port => _port;

    public OnDemandProxyService(LogService logService, IOnDemandProxyHost host)
    {
        _logService = logService;
        _host = host;
    }

    public void Start(ProxyOptions options)
    {
        if (_tcpListener != null) return;

        _options = options;
        _port = options.Port;

        try
        {
            _http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            _tcpListener = new TcpListener(IPAddress.Any, _port);
            _tcpListener.Start();
            _cts = new CancellationTokenSource();
            _ = AcceptLoopAsync(_cts.Token);
            _ = IdleLoopAsync(options.IdleSeconds, _cts.Token);
            _logService.AppLog($"On-demand proxy started on port {_port}");
        }
        catch (Exception ex)
        {
            _tcpListener = null;
            _http?.Dispose();
            _http = null;
            _logService.Error($"Failed to start on-demand proxy: {ex.Message}");
            throw;
        }
    }

    public void Stop()
    {
        if (_tcpListener == null) return;

        _cts?.Cancel();
        try { _tcpListener?.Stop(); } catch { }
        _tcpListener = null;
        _http?.Dispose();
        _http = null;
        _cts?.Dispose();
        _cts = null;

        _logService.AppLog("On-demand proxy stopped");
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var tcpClient = await _tcpListener!.AcceptTcpClientAsync(ct);
                _ = HandleClientAsync(tcpClient, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception)
            {
            }
        }
    }

    private async Task IdleLoopAsync(int idleSeconds, CancellationToken ct)
    {
        if (idleSeconds <= 0) return;

        var idleTicks = TimeSpan.FromSeconds(idleSeconds).Ticks;
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(1000, ct); }
            catch (OperationCanceledException) { break; }

            if (Interlocked.Read(ref _inflight) != 0) continue;

            var last = Interlocked.Read(ref _lastUseTicks);
            if (last == 0) continue;
            if (DateTime.UtcNow.Ticks - last <= idleTicks) continue;

            Interlocked.Exchange(ref _lastUseTicks, 0);
            try
            {
                _logService.AppLog($"On-demand proxy idle for {idleSeconds}s, stopping active server");
                await _host.StopActiveAsync();
            }
            catch (Exception ex)
            {
                _logService.Error($"On-demand proxy idle stop failed: {ex.Message}");
            }
        }
    }

    private async Task HandleClientAsync(TcpClient tcpClient, CancellationToken ct)
    {
        NetworkStream? stream = null;
        try
        {
            tcpClient.NoDelay = true;
            stream = tcpClient.GetStream();

            var (head, body) = await ReadRequestAsync(stream, ct);
            if (head == null)
            {
                tcpClient.Close();
                return;
            }

            var path = head.Path;

            if (path is "/health" or "/v1/health")
            {
                await WriteAsync(stream, ProxyProtocol.BuildSimpleResponse("200 OK", "application/json", "{\"status\":\"ok\"}"), ct);
                tcpClient.Close();
                return;
            }

            if (!IsAuthorized(head))
            {
                await WriteAsync(stream, ProxyProtocol.BuildSimpleResponse("401 Unauthorized", "application/json", "{\"error\":\"invalid api key\"}"), ct);
                tcpClient.Close();
                return;
            }

            if (path is "/v1/models" or "/models")
            {
                var json = ProxyProtocol.BuildModelsListJson(_host.GetProfileNames());
                await WriteAsync(stream, ProxyProtocol.BuildSimpleResponse("200 OK", "application/json", json), ct);
                tcpClient.Close();
                return;
            }

            if (!ProxyProtocol.IsProxiedApiPath(path))
            {
                await WriteAsync(stream, ProxyProtocol.BuildSimpleResponse("404 Not Found", "application/json", "{\"error\":\"not found\"}"), ct);
                tcpClient.Close();
                return;
            }

            var bodyText = body != null ? Encoding.UTF8.GetString(body) : "";
            var requested = ProxyProtocol.ExtractModelField(bodyText);
            var profile = ProxyProtocol.MatchProfile(requested, _host.GetProfileNames(), _host.GetFallbackProfileName());
            if (profile == null)
            {
                await WriteAsync(stream, ProxyProtocol.BuildSimpleResponse("400 Bad Request", "application/json", "{\"error\":\"no matching profile for requested model\"}"), ct);
                tcpClient.Close();
                return;
            }

            Interlocked.Increment(ref _inflight);
            Interlocked.Exchange(ref _lastUseTicks, DateTime.UtcNow.Ticks);
            try
            {
                ProxyUpstream? upstream;
                await _swapLock.WaitAsync(ct);
                try
                {
                    upstream = await _host.EnsureProfileRunningAsync(profile, ct);
                }
                finally
                {
                    _swapLock.Release();
                }

                if (upstream == null)
                {
                    await WriteAsync(stream, ProxyProtocol.BuildSimpleResponse("503 Service Unavailable", "application/json", $"{{\"error\":\"failed to start profile '{profile}'\"}}"), ct);
                    tcpClient.Close();
                    return;
                }

                await ProxyAsync(stream, head, body, upstream.Value, ct);
            }
            finally
            {
                Interlocked.Exchange(ref _lastUseTicks, DateTime.UtcNow.Ticks);
                Interlocked.Decrement(ref _inflight);
            }

            tcpClient.Close();
        }
        catch (Exception)
        {
            try { tcpClient.Close(); } catch { }
        }
        finally
        {
            stream?.Dispose();
        }
    }

    private bool IsAuthorized(ProxyRequestHead head)
    {
        var key = _options?.ApiKey;
        if (string.IsNullOrWhiteSpace(key)) return true;
        return string.Equals(head.BearerToken, key.Trim(), StringComparison.Ordinal);
    }

    private async Task ProxyAsync(NetworkStream stream, ProxyRequestHead head, byte[]? body, ProxyUpstream upstream, CancellationToken ct)
    {
        var connectHost = upstream.Host is "0.0.0.0" or "" ? "127.0.0.1" : upstream.Host;
        var uri = $"http://{connectHost}:{upstream.Port}{head.Path}";
        if (!string.IsNullOrEmpty(head.Query))
            uri += "?" + head.Query;

        using var request = new HttpRequestMessage(new HttpMethod(head.Method), uri);
        if (body is { Length: > 0 })
        {
            request.Content = new ByteArrayContent(body);
            var contentType = head.Headers.GetValueOrDefault("Content-Type") ?? "application/json";
            if (MediaTypeHeaderValue.TryParse(contentType, out var mt))
                request.Content.Headers.ContentType = mt;
        }

        HttpResponseMessage upstreamResp;
        try
        {
            upstreamResp = await _http!.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex)
        {
            _logService.Error($"On-demand proxy upstream error: {ex.Message}");
            await WriteAsync(stream, ProxyProtocol.BuildSimpleResponse("502 Bad Gateway", "application/json", "{\"error\":\"upstream request failed\"}"), ct);
            return;
        }

        using (upstreamResp)
        {
            var headerPairs = new List<KeyValuePair<string, string>>();
            foreach (var h in upstreamResp.Headers)
                headerPairs.Add(new KeyValuePair<string, string>(h.Key, string.Join(", ", h.Value)));
            foreach (var h in upstreamResp.Content.Headers)
                headerPairs.Add(new KeyValuePair<string, string>(h.Key, string.Join(", ", h.Value)));

            var headBytes = ProxyProtocol.BuildResponseHead((int)upstreamResp.StatusCode, upstreamResp.ReasonPhrase ?? "OK", headerPairs);
            await stream.WriteAsync(headBytes, ct);
            await stream.FlushAsync(ct);

            await using var upstreamStream = await upstreamResp.Content.ReadAsStreamAsync(ct);
            var buffer = new byte[16384];
            int read;
            while ((read = await upstreamStream.ReadAsync(buffer, ct)) > 0)
            {
                await stream.WriteAsync(buffer.AsMemory(0, read), ct);
                await stream.FlushAsync(ct);
                Interlocked.Exchange(ref _lastUseTicks, DateTime.UtcNow.Ticks);
            }
        }
    }

    private static async Task WriteAsync(NetworkStream stream, byte[] data, CancellationToken ct)
    {
        await stream.WriteAsync(data, ct);
        await stream.FlushAsync(ct);
    }

    private static async Task<(ProxyRequestHead? head, byte[]? body)> ReadRequestAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        using var accumulated = new MemoryStream();
        var headerEnd = -1;

        while (accumulated.Length < MaxHeaderBytes)
        {
            var n = await stream.ReadAsync(buffer, ct);
            if (n == 0) break;
            accumulated.Write(buffer, 0, n);

            var span = accumulated.GetBuffer();
            headerEnd = IndexOfHeaderEnd(span, (int)accumulated.Length);
            if (headerEnd >= 0) break;
        }

        if (headerEnd < 0) return (null, null);

        var raw = accumulated.GetBuffer();
        var total = (int)accumulated.Length;
        var headerText = Encoding.UTF8.GetString(raw, 0, headerEnd);
        var head = ProxyProtocol.ParseRequestHead(headerText);
        if (head == null) return (null, null);

        var bodyStart = headerEnd + 4;
        var contentLength = head.ContentLength;
        if (contentLength <= 0) return (head, null);
        if (contentLength > MaxBodyBytes) return (null, null);

        var body = new byte[contentLength];
        var already = Math.Min(contentLength, total - bodyStart);
        if (already > 0)
            Array.Copy(raw, bodyStart, body, 0, already);

        var offset = already;
        while (offset < contentLength)
        {
            var n = await stream.ReadAsync(body.AsMemory(offset, contentLength - offset), ct);
            if (n == 0) break;
            offset += n;
        }

        return (head, body);
    }

    private static int IndexOfHeaderEnd(byte[] data, int length)
    {
        for (var i = 0; i + 3 < length; i++)
        {
            if (data[i] == '\r' && data[i + 1] == '\n' && data[i + 2] == '\r' && data[i + 3] == '\n')
                return i;
        }
        return -1;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _swapLock.Dispose();
        _disposed = true;
    }
}
