using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using LlamaServerLauncher.Models;

namespace LlamaServerLauncher.Services;

public static class McpProbeService
{
    private const string ProtocolVersion = "2024-11-05";

    public sealed class McpProbeResult
    {
        public bool Success { get; init; }
        public List<string> Tools { get; init; } = new();
        public string Error { get; init; } = string.Empty;
    }

    public static async Task<McpProbeResult> ProbeAsync(McpServerEntry entry, int timeoutMs = 15000)
    {
        var command = entry.Command.Trim();
        if (command.Length == 0)
            return Failure("command is empty");

        var resolved = ResolveCommand(command);

        var psi = new ProcessStartInfo
        {
            FileName = resolved,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false)
        };

        foreach (var arg in McpConfigDocument.SplitArgs(entry.ArgsText))
            psi.ArgumentList.Add(arg);

        foreach (var pair in McpConfigDocument.ParseEnv(entry.EnvText))
            psi.Environment[pair.Key] = pair.Value;

        var cwd = entry.WorkingDirectory.Trim();
        if (cwd.Length > 0)
        {
            if (!Directory.Exists(cwd))
                return Failure($"working directory not found: {cwd}");
            psi.WorkingDirectory = cwd;
        }

        var stderrTail = new StringBuilder();
        Process? process = null;

        try
        {
            using var cts = new CancellationTokenSource(timeoutMs);

            try
            {
                process = Process.Start(psi);
            }
            catch (Exception ex)
            {
                return Failure($"failed to start '{command}': {ex.Message}");
            }

            if (process == null)
                return Failure($"failed to start '{command}'");

            process.ErrorDataReceived += (_, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;
                lock (stderrTail)
                {
                    if (stderrTail.Length < 2000)
                        stderrTail.AppendLine(e.Data);
                }
            };
            process.BeginErrorReadLine();

            var initialize = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = "initialize",
                ["params"] = new JsonObject
                {
                    ["protocolVersion"] = ProtocolVersion,
                    ["capabilities"] = new JsonObject(),
                    ["clientInfo"] = new JsonObject
                    {
                        ["name"] = "LlamaServerLauncher",
                        ["version"] = "1.0"
                    }
                }
            };

            await SendAsync(process, initialize, cts.Token);
            var initReply = await ReadReplyAsync(process, 1, cts.Token);
            if (initReply == null)
                return Failure(Describe("no reply to initialize", stderrTail));
            if (initReply["result"] == null)
                return Failure(Describe("initialize failed: " + RpcError(initReply), stderrTail));

            var initialized = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = "notifications/initialized"
            };
            await SendAsync(process, initialized, cts.Token);

            var listRequest = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 2,
                ["method"] = "tools/list"
            };
            await SendAsync(process, listRequest, cts.Token);

            var listReply = await ReadReplyAsync(process, 2, cts.Token);
            if (listReply == null)
                return Failure(Describe("no reply to tools/list", stderrTail));
            if (listReply["result"] == null)
                return Failure(Describe("tools/list failed: " + RpcError(listReply), stderrTail));

            var tools = new List<string>();
            if (listReply["result"]?["tools"] is JsonArray array)
            {
                foreach (var tool in array)
                {
                    var name = tool?["name"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(name))
                        tools.Add(name!);
                }
            }

            return new McpProbeResult { Success = true, Tools = tools };
        }
        catch (OperationCanceledException)
        {
            return Failure(Describe("timed out", stderrTail));
        }
        catch (Exception ex)
        {
            return Failure(Describe(ex.Message, stderrTail));
        }
        finally
        {
            if (process != null)
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch { /* the child may have exited on its own */ }
                process.Dispose();
            }
        }
    }

    public static string ResolveCommand(string command)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return command;

        if (File.Exists(command))
            return command;

        var extensions = new List<string> { string.Empty };
        var pathext = Environment.GetEnvironmentVariable("PATHEXT");
        if (string.IsNullOrWhiteSpace(pathext))
            pathext = ".COM;.EXE;.BAT;.CMD";
        foreach (var ext in pathext.Split(';', StringSplitOptions.RemoveEmptyEntries))
            extensions.Add(ext.Trim());

        bool hasDirectory = command.Contains('\\') || command.Contains('/');
        if (hasDirectory)
        {
            foreach (var ext in extensions)
            {
                var candidate = command + ext;
                if (File.Exists(candidate))
                    return candidate;
            }
            return command;
        }

        var searchPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in searchPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var ext in extensions)
            {
                string candidate;
                try
                {
                    candidate = Path.Combine(dir.Trim(), command + ext);
                }
                catch (ArgumentException)
                {
                    continue; // a malformed PATH entry
                }

                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return command;
    }

    private static async Task SendAsync(Process process, JsonObject message, CancellationToken ct)
    {
        await process.StandardInput.WriteLineAsync(message.ToJsonString().AsMemory(), ct);
        await process.StandardInput.FlushAsync(ct);
    }

    private static async Task<JsonObject?> ReadReplyAsync(Process process, int id, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var line = await process.StandardOutput.ReadLineAsync(ct);
            if (line == null)
                return null; // the child closed its stdout

            line = line.Trim();
            if (line.Length == 0)
                continue;

            JsonNode? node;
            try
            {
                node = JsonNode.Parse(line);
            }
            catch (JsonException)
            {
                continue; // servers that print plain text to stdout
            }

            if (node is not JsonObject reply)
                continue;

            if (reply["id"] is not JsonValue replyId)
                continue; // a notification

            if (replyId.TryGetValue<int>(out var numericId) && numericId == id)
                return reply;

            // the spec allows string ids, so a server may echo ours as text
            if (replyId.TryGetValue<string>(out var textId) && textId == id.ToString())
                return reply;
        }

        return null;
    }

    private static string RpcError(JsonObject reply)
    {
        var error = reply["error"];
        if (error is JsonObject errorObject)
            return errorObject["message"]?.GetValue<string>() ?? "unknown error";
        if (error is JsonValue errorValue && errorValue.TryGetValue<string>(out var text))
            return text;
        return "unknown error";
    }

    private static string Describe(string message, StringBuilder stderrTail)
    {
        string tail;
        lock (stderrTail)
        {
            tail = stderrTail.ToString().Trim();
        }

        if (tail.Length == 0)
            return message;

        return $"{message} ({tail.Replace("\r\n", " ").Replace('\n', ' ')})";
    }

    private static McpProbeResult Failure(string error) => new() { Success = false, Error = error };
}
