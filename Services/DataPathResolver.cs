using System;
using System.IO;
using System.Text.Json;

namespace LlamaServerLauncher.Services;

public class DataPathResolver
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private string? _cachedDataPath;
    private readonly string _defaultAppDataPath;

    public DataPathResolver()
    {
        _defaultAppDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LlamaServerLauncherAvalonia"
        );
    }

    public string DefaultAppDataPath => _defaultAppDataPath;

    public string PointerFilePath => Path.Combine(_defaultAppDataPath, "data-location.json");

    /// <summary>
    /// True when a custom data path is configured but the directory isn't reachable
    /// (e.g. disconnected network drive). The app then falls back to the default
    /// folder, which can look like settings were "reset".
    /// </summary>
    public bool ConfiguredCustomPathMissing { get; private set; }

    public string ResolveDataPath()
    {
        if (_cachedDataPath != null)
            return _cachedDataPath;

        _cachedDataPath = ReadCustomPathFromPointer() ?? _defaultAppDataPath;
        return _cachedDataPath;
    }

    public void InvalidateCache()
    {
        _cachedDataPath = null;
    }

    public bool IsCustomPathActive()
    {
        var resolved = ResolveDataPath();
        return !string.Equals(
            Path.GetFullPath(resolved).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(_defaultAppDataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    public void SetCustomPath(string customPath)
    {
        Directory.CreateDirectory(_defaultAppDataPath);
        var data = new DataLocationInfo { CustomDataPath = customPath };
        var json = JsonSerializer.Serialize(data, JsonOptions);

        // Atomic write: a half-written pointer would silently reset the data path.
        var tempPath = $"{PointerFilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(tempPath, json);
            if (File.Exists(PointerFilePath))
                File.Move(tempPath, PointerFilePath, overwrite: true);
            else
                File.Move(tempPath, PointerFilePath);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* best-effort cleanup */ }
            }
        }

        _cachedDataPath = customPath;
    }

    public void ClearCustomPath()
    {
        if (File.Exists(PointerFilePath))
        {
            File.Delete(PointerFilePath);
        }
        _cachedDataPath = _defaultAppDataPath;
    }

    private string? ReadCustomPathFromPointer()
    {
        ConfiguredCustomPathMissing = false;

        if (!File.Exists(PointerFilePath))
            return null;

        try
        {
            var json = File.ReadAllText(PointerFilePath);
            var info = JsonSerializer.Deserialize<DataLocationInfo>(json);
            if (!string.IsNullOrWhiteSpace(info?.CustomDataPath))
            {
                if (Directory.Exists(info!.CustomDataPath))
                    return info.CustomDataPath;

                // Configured but unreachable - let the app warn instead of falling back silently.
                ConfiguredCustomPathMissing = true;
            }
        }
        catch
        {
        }

        return null;
    }

    private class DataLocationInfo
    {
        public string? CustomDataPath { get; set; }
    }
}
