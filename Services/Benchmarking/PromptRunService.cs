using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LlamaServerLauncher.Models.Benchmarking;

namespace LlamaServerLauncher.Services.Benchmarking;

public sealed class PromptRunOptions
{
    public string SystemPrompt { get; set; } = string.Empty;
    public string PromptsText { get; set; } = string.Empty;
    public bool KeepContext { get; set; } = true;

    public int MaxTokens { get; set; }

    public int TimeoutSeconds { get; set; } = 600;
    public string? ApiKey { get; set; }

    public string? PreferredModel { get; set; }
}

public sealed class PromptRunService
{
    private readonly LogService? _log;
    private readonly HttpClient _http;

    public PromptRunService(LogService? log = null, HttpClient? httpClient = null)
    {
        _log = log;
        _http = httpClient ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task RunAsync(string baseUrl, PromptRunOptions options, PromptRunReport report, CancellationToken ct)
    {
        var prompts = PromptRunDocument.SplitPrompts(options.PromptsText);
        if (prompts.Count == 0)
            return;

        var url = baseUrl.TrimEnd('/') + "/v1/chat/completions";
        var model = await ResolveModelAsync(baseUrl, options, ct);
        var history = new List<(string Role, string Content)>();
        if (!string.IsNullOrWhiteSpace(options.SystemPrompt))
            history.Add(("system", options.SystemPrompt.Trim()));
        int systemCount = history.Count;

        for (int i = 0; i < prompts.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (!options.KeepContext)
                history.RemoveRange(systemCount, history.Count - systemCount);

            history.Add(("user", prompts[i]));

            var turn = new PromptRunTurn { Index = i + 1, Prompt = prompts[i] };
            var sw = Stopwatch.StartNew();
            try
            {
                var answer = await SendAsync(url, history, options, model, ct);
                sw.Stop();
                turn.Response = answer.Content;
                turn.Reasoning = answer.Reasoning;
                turn.FinishReason = answer.FinishReason;
                turn.PromptTokens = answer.PromptTokens;
                turn.PredictedTokens = answer.PredictedTokens;
                turn.PromptTps = answer.PromptTps;
                turn.GenTps = answer.GenTps;
                turn.TtftMs = answer.PromptMs;
                turn.DurationSeconds = sw.Elapsed.TotalSeconds;

                history.Add(("assistant", answer.Content));
                _log?.AppLog($"Prompt run {turn.Index}/{prompts.Count}: "
                    + $"{answer.PredictedTokens?.ToString() ?? "?"} tokens, {answer.GenTps ?? 0:F1} tok/s");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                sw.Stop();
                turn.DurationSeconds = sw.Elapsed.TotalSeconds;
                turn.Error = "Interrupted.";
                report.Turns.Add(turn);
                throw;
            }
            catch (Exception ex)
            {
                sw.Stop();
                turn.DurationSeconds = sw.Elapsed.TotalSeconds;
                turn.Error = ex.Message;
                history.RemoveAt(history.Count - 1);
                _log?.Error($"Prompt run request {turn.Index} failed: {ex.Message}");
            }

            report.Turns.Add(turn);
        }
    }

    private async Task<string?> ResolveModelAsync(string baseUrl, PromptRunOptions options, CancellationToken ct)
    {
        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl.TrimEnd('/') + "/v1/models");
            if (!string.IsNullOrWhiteSpace(options.ApiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, linked.Token);
            if (!response.IsSuccessStatusCode)
                return null;

            var ids = ModelListResponse.Parse(await response.Content.ReadAsStringAsync(linked.Token));
            var chosen = ModelListResponse.Choose(ids, options.PreferredModel);
            if (chosen != null && ids.Count > 1)
                _log?.AppLog($"Prompt run: the server offers {ids.Count} models, sending requests to '{chosen}'.");
            return chosen;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log?.AppLog($"Prompt run: could not read the model list ({ex.Message}), sending requests without a model name.");
            return null;
        }
    }

    private async Task<ChatCompletionResponse> SendAsync(
        string url,
        List<(string Role, string Content)> history,
        PromptRunOptions options,
        string? model,
        CancellationToken ct)
    {
        var json = BuildRequestJson(history, options.MaxTokens, model);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(5, options.TimeoutSeconds)));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);

        HttpResponseMessage response;
        string body;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, linked.Token);
            body = await response.Content.ReadAsStringAsync(linked.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new TimeoutException($"No answer within {Math.Max(5, options.TimeoutSeconds)} s.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {ChatCompletionResponse.ExtractError(body)}");

            return ChatCompletionResponse.Parse(body);
        }
    }

    private static string BuildRequestJson(List<(string Role, string Content)> history, int maxTokens, string? model)
    {
        var buffer = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("stream", false);
            if (!string.IsNullOrWhiteSpace(model))
                writer.WriteString("model", model);
            if (maxTokens > 0)
                writer.WriteNumber("max_tokens", maxTokens);
            writer.WriteStartArray("messages");
            foreach (var (role, content) in history)
            {
                writer.WriteStartObject();
                writer.WriteString("role", role);
                writer.WriteString("content", content);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
