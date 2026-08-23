using System;
using System.Collections.Generic;
using System.IO;
using LlamaServerLauncher.Models;

namespace LlamaServerLauncher.Services;

public class McpConfigService
{
    private readonly LogService? _logService;

    public McpConfigService(LogService? logService = null, string? appDataPath = null)
    {
        _logService = logService;
        var basePath = appDataPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LlamaServerLauncherAvalonia"
        );
        McpDirectory = Path.Combine(basePath, "mcp");
    }

    public string McpDirectory { get; }

    public string GetConfigPath(string profileName)
    {
        var name = string.IsNullOrWhiteSpace(profileName) ? "Unnamed" : profileName;
        var safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        if (safeName.Length == 0)
            safeName = "Unnamed";
        return Path.Combine(McpDirectory, $"{safeName}.json");
    }

    public string? Materialize(ServerConfiguration config, string profileName)
    {
        var path = GetConfigPath(profileName);

        if (!McpConfigDocument.HasUsableServers(config))
        {
            TryDelete(path);
            return null;
        }

        var json = McpConfigDocument.BuildJson(McpConfigDocument.EnabledServers(config));

        try
        {
            Directory.CreateDirectory(McpDirectory);

            var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(tempPath, json);
                if (File.Exists(path))
                    File.Move(tempPath, path, overwrite: true);
                else
                    File.Move(tempPath, path);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { /* best-effort cleanup */ }
                }
            }

            _logService?.Info($"MCP config written for profile '{profileName}': {path}");
            return path;
        }
        catch (Exception ex)
        {
            _logService?.Error($"Failed to write MCP config for profile '{profileName}': {ex.Message}");
            return null;
        }
    }

    public void DeleteConfig(string profileName)
    {
        TryDelete(GetConfigPath(profileName));
    }

    public static List<McpServerEntry> ImportFromFile(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return McpConfigDocument.Parse(json);
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            _logService?.Error($"Failed to remove stale MCP config '{path}': {ex.Message}");
        }
    }
}
