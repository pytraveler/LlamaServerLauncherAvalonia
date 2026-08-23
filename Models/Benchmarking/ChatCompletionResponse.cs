using System.Text.Json;

namespace LlamaServerLauncher.Models.Benchmarking;

public sealed class ChatCompletionResponse
{
    public string Content { get; set; } = string.Empty;
    public string? Reasoning { get; set; }
    public string? FinishReason { get; set; }
    public int? PromptTokens { get; set; }
    public int? PredictedTokens { get; set; }
    public double? PromptTps { get; set; }
    public double? GenTps { get; set; }
    public double? PromptMs { get; set; }

    public static ChatCompletionResponse Parse(string body)
    {
        var result = new ChatCompletionResponse();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (root.TryGetProperty("choices", out var choices)
            && choices.ValueKind == JsonValueKind.Array
            && choices.GetArrayLength() > 0)
        {
            var choice = choices[0];
            if (choice.TryGetProperty("finish_reason", out var fr) && fr.ValueKind == JsonValueKind.String)
                result.FinishReason = fr.GetString();
            if (choice.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object)
            {
                if (message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                    result.Content = content.GetString() ?? string.Empty;
                if (message.TryGetProperty("reasoning_content", out var reasoning) && reasoning.ValueKind == JsonValueKind.String)
                    result.Reasoning = reasoning.GetString();
            }
        }

        if (root.TryGetProperty("timings", out var timings) && timings.ValueKind == JsonValueKind.Object)
        {
            result.PromptTokens = GetInt(timings, "prompt_n");
            result.PredictedTokens = GetInt(timings, "predicted_n");
            result.PromptTps = GetDouble(timings, "prompt_per_second");
            result.GenTps = GetDouble(timings, "predicted_per_second");
            result.PromptMs = GetDouble(timings, "prompt_ms");
        }

        if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            result.PromptTokens ??= GetInt(usage, "prompt_tokens");
            result.PredictedTokens ??= GetInt(usage, "completion_tokens");
        }

        return result;
    }

    public static string ExtractError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String)
                    return error.GetString() ?? body;
                if (error.ValueKind == JsonValueKind.Object
                    && error.TryGetProperty("message", out var message)
                    && message.ValueKind == JsonValueKind.String)
                    return message.GetString() ?? body;
            }
        }
        catch
        {
            // not json
        }
        return body.Length > 400 ? body.Substring(0, 400) : body;
    }

    private static int? GetInt(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var v)
            ? v
            : (int?)null;

    private static double? GetDouble(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number
            ? el.GetDouble()
            : (double?)null;
}
