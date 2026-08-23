using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LlamaServerLauncher.Models;

public class ServerToolInfo
{
    public string Tool { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public bool IsMcp => string.Equals(Type, "mcp", StringComparison.OrdinalIgnoreCase);
}

public static class ServerToolsResponse
{
    public static List<ServerToolInfo> Parse(string json)
    {
        var result = new List<ServerToolInfo>();
        if (string.IsNullOrWhiteSpace(json))
            return result;

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return result;
        }

        if (root is not JsonArray array)
            return result;

        foreach (var node in array)
        {
            if (node is not JsonObject item)
                continue;

            var tool = GetString(item, "tool");
            if (tool.Length == 0)
                continue;

            result.Add(new ServerToolInfo
            {
                Tool = tool,
                DisplayName = GetString(item, "display_name"),
                Type = GetString(item, "type")
            });
        }

        return result;
    }

    private static string GetString(JsonObject node, string key)
    {
        return node[key] is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : string.Empty;
    }
}
