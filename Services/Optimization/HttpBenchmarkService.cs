using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LlamaServerLauncher.Services.Optimization;

public sealed class HttpBenchmarkResult
{
    public double TgTs { get; init; }

    public double PpTs { get; init; }

    public double TimeToFirstTokenMs { get; init; }
}

public sealed class HttpBenchmarkService
{
    private readonly HttpClient _http;
    private readonly LogService? _log;

    public HttpBenchmarkService(HttpClient? httpClient = null, LogService? log = null)
    {
        _http = httpClient ?? new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        _log = log;
    }

    public async Task<bool> CheckHealthOnceAsync(string baseUrl, CancellationToken ct)
    {
        var healthUrl = baseUrl.TrimEnd('/') + "/health";
        try
        {
            using var resp = await _http.GetAsync(healthUrl, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (HttpRequestException) { return false; }
    }

    public async Task<bool> WaitForHealthAsync(string baseUrl, int timeoutSeconds, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        var healthUrl = baseUrl.TrimEnd('/') + "/health";
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var resp = await _http.GetAsync(healthUrl, ct);
                if (resp.IsSuccessStatusCode)
                    return true;
            }
            catch (HttpRequestException) {}

            await Task.Delay(500, ct);
        }
        return false;
    }

    public async Task<HttpBenchmarkResult> MeasureAsync(
        string baseUrl,
        string prompt,
        int nPredict,
        int timeoutSeconds,
        CancellationToken ct)
    {
        var url = baseUrl.TrimEnd('/') + "/completion";
        var payload = new
        {
            prompt,
            n_predict = nPredict,
            stream = false,
            cache_prompt = false,
            temperature = 0.0,
        };
        var json = JsonSerializer.Serialize(payload);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync(url, content, linked.Token);
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadAsStringAsync(linked.Token);
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("timings", out var timings))
            throw new InvalidOperationException("llama-server response has no 'timings' object.");

        double tg = GetDouble(timings, "predicted_per_second");
        double pp = GetDouble(timings, "prompt_per_second");
        double promptMs = GetDouble(timings, "prompt_ms");

        return new HttpBenchmarkResult { TgTs = tg, PpTs = pp, TimeToFirstTokenMs = promptMs };
    }

    private static double GetDouble(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number ? el.GetDouble() : 0.0;
}
