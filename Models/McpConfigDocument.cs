using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LlamaServerLauncher.Models;

public enum McpIssueKind
{
    EmptyName,
    EmptyCommand,
    DuplicateName,
    InvalidTimeout
}

public readonly struct McpConfigIssue
{
    public McpConfigIssue(McpIssueKind kind, string serverName)
    {
        Kind = kind;
        ServerName = serverName;
    }

    public McpIssueKind Kind { get; }
    public string ServerName { get; }
}

public static class McpConfigDocument
{
    public const int DefaultTimeoutMs = 30000;

    public static List<McpServerEntry> EnabledServers(ServerConfiguration config)
    {
        var result = new List<McpServerEntry>();
        if (config.McpServers == null)
            return result;

        foreach (var entry in config.McpServers)
        {
            if (entry == null || !entry.Enabled)
                continue;
            if (string.IsNullOrWhiteSpace(entry.Name) || string.IsNullOrWhiteSpace(entry.Command))
                continue;
            result.Add(entry);
        }

        return result;
    }

    public static bool HasUsableServers(ServerConfiguration config)
    {
        return config.McpEnabled && EnabledServers(config).Count > 0;
    }

    public static string BuildJson(IEnumerable<McpServerEntry> entries)
    {
        var servers = new JsonObject();

        foreach (var entry in entries)
        {
            if (entry == null)
                continue;

            var name = entry.Name.Trim();
            var command = entry.Command.Trim();
            if (name.Length == 0 || command.Length == 0)
                continue;

            var node = new JsonObject { ["command"] = command };

            var args = SplitArgs(entry.ArgsText);
            if (args.Count > 0)
            {
                var array = new JsonArray();
                foreach (var arg in args)
                {
                    array.Add(JsonValue.Create(arg));
                }
                node["args"] = array;
            }

            var env = ParseEnv(entry.EnvText);
            if (env.Count > 0)
            {
                var envNode = new JsonObject();
                foreach (var pair in env)
                    envNode[pair.Key] = pair.Value;
                node["env"] = envNode;
            }

            var cwd = entry.WorkingDirectory.Trim();
            if (cwd.Length > 0)
                node["cwd"] = cwd;

            if (entry.TimeoutMs is int timeout && timeout > 0)
                node["timeout_ms"] = timeout;

            servers[name] = node;
        }

        var root = new JsonObject { ["mcpServers"] = servers };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public static List<McpServerEntry> Parse(string json)
    {
        var result = new List<McpServerEntry>();
        if (string.IsNullOrWhiteSpace(json))
            return result;

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            throw new FormatException("invalid JSON");
        }

        if (root is not JsonObject rootObject)
            return result;

        JsonObject? servers = rootObject["mcpServers"] as JsonObject;
        if (servers == null)
        {
            bool looksLikeServers = rootObject.Count > 0;
            foreach (var pair in rootObject)
            {
                if (pair.Value is not JsonObject candidate || candidate["command"] == null)
                {
                    looksLikeServers = false;
                    break;
                }
            }

            if (!looksLikeServers)
                return result;

            servers = rootObject;
        }

        foreach (var pair in servers)
        {
            if (pair.Value is not JsonObject node)
                continue;

            var entry = new McpServerEntry
            {
                Name = pair.Key,
                Command = GetString(node, "command"),
                WorkingDirectory = GetString(node, "cwd"),
                Enabled = true
            };

            if (node["args"] is JsonArray args)
            {
                var values = new List<string>();
                foreach (var arg in args)
                {
                    var text = arg?.GetValue<object>()?.ToString();
                    if (text != null)
                        values.Add(text);
                }
                entry.ArgsText = FormatArgs(values);
            }

            if (node["env"] is JsonObject env)
            {
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var variable in env)
                {
                    var text = variable.Value?.GetValue<object>()?.ToString();
                    if (text != null)
                        values[variable.Key] = text;
                }
                entry.EnvText = FormatEnv(values);
            }

            if (node["timeout_ms"] is JsonValue timeout && timeout.TryGetValue<int>(out var timeoutMs) && timeoutMs > 0)
                entry.TimeoutMs = timeoutMs;

            if (node["disabled"] is JsonValue disabled && disabled.TryGetValue<bool>(out var isDisabled) && isDisabled)
                entry.Enabled = false;

            if (node["enabled"] is JsonValue enabled && enabled.TryGetValue<bool>(out var isEnabled) && !isEnabled)
                entry.Enabled = false;

            result.Add(entry);
        }

        return result;
    }

    public static List<McpConfigIssue> Validate(IEnumerable<McpServerEntry> entries)
    {
        var issues = new List<McpConfigIssue>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            if (entry == null || !entry.Enabled)
                continue;

            var name = entry.Name.Trim();
            if (name.Length == 0)
            {
                issues.Add(new McpConfigIssue(McpIssueKind.EmptyName, string.Empty));
            }
            else if (!seen.Add(name))
            {
                issues.Add(new McpConfigIssue(McpIssueKind.DuplicateName, name));
            }

            if (entry.Command.Trim().Length == 0)
                issues.Add(new McpConfigIssue(McpIssueKind.EmptyCommand, name));

            if (entry.TimeoutMs is int timeout && timeout <= 0)
                issues.Add(new McpConfigIssue(McpIssueKind.InvalidTimeout, name));
        }

        return issues;
    }

    public static List<string> SplitArgs(string? argsText)
    {
        if (string.IsNullOrWhiteSpace(argsText))
            return new List<string>();

        return CommandLineParser.ParseArguments(argsText.Replace('\r', ' ').Replace('\n', ' '));
    }

    public static string FormatArgs(IEnumerable<string> args)
    {
        var parts = new List<string>();
        foreach (var arg in args)
            parts.Add(QuoteIfNeeded(arg));
        return string.Join(" ", parts);
    }

    public static Dictionary<string, string> ParseEnv(string? envText)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(envText))
            return result;

        var lines = envText.Split('\n');
        foreach (var raw in lines)
        {
            var line = raw.Trim('\r', ' ', '\t');
            if (line.Length == 0 || line[0] == '#')
                continue;

            int sep = line.IndexOf('=');
            if (sep <= 0)
                continue;

            var key = line.Substring(0, sep).Trim();
            var value = line.Substring(sep + 1).Trim();
            if (key.Length == 0)
                continue;

            result[key] = value;
        }

        return result;
    }

    public static string FormatEnv(IDictionary<string, string> env)
    {
        var sb = new StringBuilder();
        foreach (var pair in env)
        {
            if (sb.Length > 0)
                sb.Append('\n');
            sb.Append(pair.Key).Append('=').Append(pair.Value);
        }
        return sb.ToString();
    }

    private static string GetString(JsonObject node, string key)
    {
        return node[key] is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : string.Empty;
    }

    private static string QuoteIfNeeded(string value)
    {
        if (value.Length == 0)
            return "\"\"";

        if (value.IndexOf(' ') < 0 && value.IndexOf('\t') < 0 && value.IndexOf('"') < 0)
            return value;

        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }
}
