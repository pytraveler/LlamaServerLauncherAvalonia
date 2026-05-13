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
        File.WriteAllText(PointerFilePath, json);
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
        if (!File.Exists(PointerFilePath))
            return null;

        try
        {
            var json = File.ReadAllText(PointerFilePath);
            var info = JsonSerializer.Deserialize<DataLocationInfo>(json);
            if (info?.CustomDataPath != null && Directory.Exists(info.CustomDataPath))
                return info.CustomDataPath;
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
