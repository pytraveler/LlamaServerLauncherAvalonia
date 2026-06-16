using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LlamaServerLauncher.Models;

namespace LlamaServerLauncher.Services;

public class ConfigurationService
{
    private readonly string _profilesPath;
    private readonly string _scenariosPath;
    private readonly string _appSettingsPath;
    private readonly LogService _logService;
    private readonly SemaphoreSlim _appSettingsSaveLock = new(1, 1);

    /// <summary>Raised with the full file path when a profile/scenario file is skipped because it can't be read.</summary>
    public event Action<string>? CorruptFileSkipped;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public ConfigurationService(LogService logService, string? appDataPath = null)
    {
        _logService = logService;
        var basePath = appDataPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LlamaServerLauncherAvalonia"
        );
        _profilesPath = Path.Combine(basePath, "profiles");
        _scenariosPath = Path.Combine(basePath, "scenarios");
        _appSettingsPath = Path.Combine(basePath, "app.json");
        Directory.CreateDirectory(_profilesPath);
        Directory.CreateDirectory(_scenariosPath);
    }

    /// <summary>True when app.json existed but couldn't be read this session (e.g. locked).</summary>
    public bool LoadFailedKeepExisting { get; private set; }

    public async Task SaveAppSettingsAsync(AppSettings settings)
    {
        // Serialized + atomic + .bak so a crash mid-write can never wipe all settings.
        await _appSettingsSaveLock.WaitAsync();
        try
        {
            await WriteJsonAtomicAsync(_appSettingsPath, settings, keepBackup: true);
            _logService.Info("Application settings saved successfully");
        }
        catch (Exception ex)
        {
            _logService.Error($"Failed to save application settings: {ex.Message}");
        }
        finally
        {
            _appSettingsSaveLock.Release();
        }
    }

    public async Task<AppSettings> LoadAppSettingsAsync()
    {
        LoadFailedKeepExisting = false;

        // No file yet -> fresh install, defaults are fine.
        if (!File.Exists(_appSettingsPath))
            return new AppSettings();

        var (settings, ioError) = await TryReadJsonAsync<AppSettings>(_appSettingsPath);
        if (settings != null)
        {
            _logService.Info("Application settings loaded successfully");
            return settings;
        }

        if (ioError)
        {
            // Locked/transient: leave the file alone, don't overwrite it with defaults.
            _logService.Error("app.json could not be read; keeping the existing file and using defaults for this session");
            LoadFailedKeepExisting = true;
            return new AppSettings();
        }

        // Corrupt: quarantine and try the backup.
        _logService.Error("app.json is corrupt; quarantining it and attempting recovery from backup");
        QuarantineFile(_appSettingsPath);

        var backupPath = _appSettingsPath + ".bak";
        if (File.Exists(backupPath))
        {
            var (backup, _) = await TryReadJsonAsync<AppSettings>(backupPath);
            if (backup != null)
            {
                _logService.Warning("Recovered application settings from backup (app.json.bak)");
                return backup;
            }
        }

        _logService.Error("No valid backup available; starting from default settings");
        return new AppSettings();
    }

    /// <summary>
    /// Atomic JSON write: dump to a unique temp file in the same directory, then rename
    /// over the target. Optionally keeps a copy of the previous file as &lt;path&gt;.bak.
    /// </summary>
    private async Task WriteJsonAtomicAsync<T>(string path, T value, bool keepBackup = false)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(tempPath, json);

            if (File.Exists(path))
            {
                if (keepBackup)
                {
                    try { File.Copy(path, path + ".bak", overwrite: true); }
                    catch (Exception ex) { _logService.Warning($"Could not update backup for '{Path.GetFileName(path)}': {ex.Message}"); }
                }
                File.Move(tempPath, path, overwrite: true);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* best-effort cleanup */ }
            }
        }
    }

    /// <summary>
    /// Reads JSON with a few quick retries for transient IO errors (antivirus locks etc.).
    /// ioError is true only when the file exists but couldn't be read, so callers can tell
    /// "locked" apart from "corrupt" and avoid clobbering a file they couldn't open.
    /// </summary>
    private async Task<(T? value, bool ioError)> TryReadJsonAsync<T>(string path) where T : class
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                var json = await File.ReadAllTextAsync(path);
                return (JsonSerializer.Deserialize<T>(json), false);
            }
            catch (IOException) when (attempt < 2)
            {
                await Task.Delay(120);
            }
            catch (IOException ex)
            {
                _logService.Error($"IO error reading '{Path.GetFileName(path)}': {ex.Message}");
                return (null, true);
            }
            catch (Exception ex)
            {
                _logService.Error($"Failed to parse '{Path.GetFileName(path)}': {ex.Message}");
                return (null, false);
            }
        }
    }

    private void QuarantineFile(string path)
    {
        try
        {
            var quarantinePath = $"{path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Copy(path, quarantinePath, overwrite: true);
            _logService.Warning($"Saved a copy of the corrupt file as '{Path.GetFileName(quarantinePath)}'");
        }
        catch (Exception ex)
        {
            _logService.Error($"Failed to quarantine corrupt file '{Path.GetFileName(path)}': {ex.Message}");
        }
    }

    /// <summary>Deletes a single file by full path (used to remove a damaged profile/scenario file).</summary>
    public void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
            _logService.Info($"Deleted file '{Path.GetFileName(path)}'");
        }
    }

    public async Task SaveProfileAsync(string name, ServerConfiguration config)
    {
        try
        {
            var profile = new ProfileInfo
            {
                Name = name,
                FilePath = GetProfilePath(name),
                LastModified = DateTime.Now,
                Configuration = config
            };

            var filePath = GetProfilePath(name);
            await WriteJsonAtomicAsync(filePath, profile);
            _logService.Info($"Profile '{name}' saved successfully");
        }
        catch (Exception ex)
        {
            _logService.Error($"Failed to save profile '{name}': {ex.Message}");
            throw;
        }
    }

    public async Task<ServerConfiguration?> LoadProfileAsync(string name)
    {
        try
        {
            var filePath = GetProfilePath(name);
            if (!File.Exists(filePath))
            {
                _logService.Warning($"Profile '{name}' not found");
                return null;
            }

            var json = await File.ReadAllTextAsync(filePath);
            var profile = JsonSerializer.Deserialize<ProfileInfo>(json);
            _logService.Info($"Profile '{name}' loaded successfully");
            return profile?.Configuration;
        }
        catch (Exception ex)
        {
            _logService.Error($"Failed to load profile '{name}': {ex.Message}");
            return null;
        }
    }

    public async Task<ServerConfiguration?> LoadProfileFromFileAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                _logService.Warning($"Profile file not found: {filePath}");
                return null;
            }

            var json = await File.ReadAllTextAsync(filePath);
            var profile = JsonSerializer.Deserialize<ProfileInfo>(json);
            
            if (profile == null || profile.Configuration == null)
            {
                _logService.Warning($"Invalid profile format in file: {filePath}");
                return null;
            }

            _logService.Info($"Profile from file loaded: {filePath}");
            return profile.Configuration;
        }
        catch (JsonException ex)
        {
            _logService.Error($"JSON parsing error: {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            _logService.Error($"Failed to load profile from file '{filePath}': {ex.Message}");
            return null;
        }
    }

    public List<ProfileInfo> GetAllProfiles()
    {
        var profiles = new List<ProfileInfo>();

        try
        {
            var files = Directory.GetFiles(_profilesPath, "*.json");
            foreach (var file in files)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var profile = JsonSerializer.Deserialize<ProfileInfo>(json);
                    if (profile != null)
                    {
                        profile.FilePath = file;
                        profiles.Add(profile);
                    }
                }
                catch (Exception ex)
                {
                    _logService.Warning($"Skipped unreadable profile '{Path.GetFileName(file)}': {ex.Message}");
                    CorruptFileSkipped?.Invoke(file);
                }
            }
        }
        catch (Exception ex)
        {
            _logService.Error($"Failed to get profiles: {ex.Message}");
        }

        return profiles.OrderBy(p => p.Name).ToList();
    }

    public async Task DeleteProfileAsync(string name)
    {
        try
        {
            var filePath = GetProfilePath(name);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _logService.Info($"Profile '{name}' deleted");
            }

            await RemoveProfileFromScenariosAsync(name);
        }
        catch (Exception ex)
        {
            _logService.Error($"Failed to delete profile '{name}': {ex.Message}");
            throw;
        }
    }

    public async Task RenameProfileAsync(string oldName, string newName)
    {
        try
        {
            var oldFilePath = GetProfilePath(oldName);
            var newFilePath = GetProfilePath(newName);
            
            if (!File.Exists(oldFilePath))
            {
                _logService.Warning($"Profile '{oldName}' not found");
                throw new FileNotFoundException($"Profile '{oldName}' not found");
            }
            
            if (File.Exists(newFilePath))
            {
                _logService.Warning($"Profile '{newName}' already exists");
                throw new InvalidOperationException($"Profile '{newName}' already exists");
            }
            
            var json = await File.ReadAllTextAsync(oldFilePath);
            var profile = JsonSerializer.Deserialize<ProfileInfo>(json);
            
            if (profile != null)
            {
                profile.Name = newName;
                profile.LastModified = DateTime.Now;
                profile.FilePath = newFilePath;

                await WriteJsonAtomicAsync(newFilePath, profile);
                File.Delete(oldFilePath);
                _logService.Info($"Profile renamed from '{oldName}' to '{newName}'");

                await RenameProfileInScenariosAsync(oldName, newName);
            }
        }
        catch (Exception ex)
        {
            _logService.Error($"Failed to rename profile '{oldName}': {ex.Message}");
            throw;
        }
    }

    public async Task ExportProfileAsync(string filePath, ServerConfiguration config)
    {
        var profile = new ProfileInfo
        {
            Name = Path.GetFileNameWithoutExtension(filePath),
            FilePath = filePath,
            LastModified = DateTime.Now,
            Configuration = config
        };

        var json = JsonSerializer.Serialize(profile, JsonOptions);
        await File.WriteAllTextAsync(filePath, json);
        _logService.Info($"Profile exported to '{filePath}'");
    }

    public async Task<ServerConfiguration?> ImportProfileAsync(string filePath)
    {
        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var profile = JsonSerializer.Deserialize<ProfileInfo>(json);
            _logService.Info($"Profile imported from '{filePath}'");
            return profile?.Configuration;
        }
        catch (Exception ex)
        {
            _logService.Error($"Failed to import profile: {ex.Message}");
            return null;
        }
    }

    private string GetProfilePath(string name)
    {
        var safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_profilesPath, $"{safeName}.json");
    }

    public class ExportResult
    {
        public int ExportedCount { get; set; }
        public int SkippedCount { get; set; }
        public string? ErrorMessage { get; set; }
        public bool Success => string.IsNullOrEmpty(ErrorMessage);
    }

    public class ImportResult
    {
        public int ImportedCount { get; set; }
        public int SkippedCount { get; set; }
        public string? ErrorMessage { get; set; }
        public bool Success => string.IsNullOrEmpty(ErrorMessage);
    }

    public async Task ExportAllProfilesAsync(string zipFilePath)
    {
        var profiles = GetAllProfiles();
        if (profiles.Count == 0)
        {
            throw new InvalidOperationException("No profiles to export");
        }

        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            Directory.CreateDirectory(tempDir);

            // Create metadata file
            var metadata = new
            {
                Version = 1,
                ExportedAt = DateTime.Now,
                ProfileCount = profiles.Count
            };
            var metadataJson = JsonSerializer.Serialize(metadata, JsonOptions);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "metadata.json"), metadataJson);

            // Write each profile as a separate JSON file
            foreach (var profile in profiles)
            {
                var profileJson = JsonSerializer.Serialize(profile, JsonOptions);
                var safeFileName = GetSafeFileName(profile.Name) + ".json";
                await File.WriteAllTextAsync(Path.Combine(tempDir, safeFileName), profileJson);
            }

            // Create ZIP archive
            if (File.Exists(zipFilePath))
            {
                File.Delete(zipFilePath);
            }
            ZipFile.CreateFromDirectory(tempDir, zipFilePath);
            _logService.Info($"Exported {profiles.Count} profiles to '{zipFilePath}'");
        }
        finally
        {
            // Clean up temp directory
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    public async Task<ImportResult> ImportAllProfilesAsync(string zipFilePath)
    {
        var result = new ImportResult { ImportedCount = 0, SkippedCount = 0 };

        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            if (!File.Exists(zipFilePath))
            {
                result.ErrorMessage = "File not found";
                return result;
            }

            // Extract ZIP
            Directory.CreateDirectory(tempDir);
            ZipFile.ExtractToDirectory(zipFilePath, tempDir);

            // Get existing profile names
            var existingProfiles = GetAllProfiles().Select(p => p.Name.ToLowerInvariant()).ToHashSet();

            // Process each JSON file (except metadata.json)
            var files = Directory.GetFiles(tempDir, "*.json");
            foreach (var file in files)
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                if (fileName.Equals("metadata", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    var profile = JsonSerializer.Deserialize<ProfileInfo>(json);
                    if (profile?.Configuration == null)
                        continue;

                    // Generate unique name if conflict exists
                    var originalName = profile.Name;
                    var newName = GetUniqueProfileName(profile.Name, existingProfiles);
                    
                    if (!newName.Equals(originalName, StringComparison.OrdinalIgnoreCase))
                    {
                        result.SkippedCount++;
                    }

                    // Update profile name and save
                    profile.Name = newName;
                    profile.LastModified = DateTime.Now;
                    profile.FilePath = GetProfilePath(newName);

                    await WriteJsonAtomicAsync(profile.FilePath, profile);

                    existingProfiles.Add(newName.ToLowerInvariant());
                    result.ImportedCount++;
                    _logService.Info($"Imported profile '{newName}' from backup");
                }
                catch (Exception ex)
                {
                    _logService.Warning($"Failed to import profile from '{file}': {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _logService.Error($"Failed to import profiles from backup: {ex.Message}");
        }
        finally
        {
            // Clean up temp directory
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }

        return result;
    }

    private string GetUniqueProfileName(string baseName, HashSet<string> existingNames)
    {
        if (!existingNames.Contains(baseName.ToLowerInvariant()))
        {
            return baseName;
        }

        // Try adding " (IMPORTED)" suffix
        var importedName = $"{baseName} (IMPORTED)";
        if (!existingNames.Contains(importedName.ToLowerInvariant()))
        {
            return importedName;
        }

        // Try with numeric suffix
        int counter = 1;
        string newName;
        do
        {
            counter++;
            newName = $"{baseName} ({counter})";
        } while (existingNames.Contains(newName.ToLowerInvariant()));

        return newName;
    }

    private string GetSafeFileName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return string.Join("_", name.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
    }

    public async Task<List<ScenarioInfo>> GetAllScenariosAsync()
    {
        var scenarios = new List<ScenarioInfo>();

        try
        {
            var files = Directory.GetFiles(_scenariosPath, "*.json");
            foreach (var file in files)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    var scenario = JsonSerializer.Deserialize<ScenarioInfo>(json);
                    if (scenario != null)
                        scenarios.Add(scenario);
                }
                catch (Exception ex)
                {
                    _logService.Warning($"Skipped unreadable scenario '{Path.GetFileName(file)}': {ex.Message}");
                    CorruptFileSkipped?.Invoke(file);
                }
            }
        }
        catch (Exception ex)
        {
            _logService.Error($"Failed to get scenarios: {ex.Message}");
        }

        return scenarios.OrderBy(s => s.Name).ToList();
    }

    public async Task SaveScenarioAsync(ScenarioInfo scenario)
    {
        try
        {
            var filePath = GetScenarioPath(scenario.Name);
            await WriteJsonAtomicAsync(filePath, scenario);
            _logService.Info($"Scenario '{scenario.Name}' saved successfully");
        }
        catch (Exception ex)
        {
            _logService.Error($"Failed to save scenario '{scenario.Name}': {ex.Message}");
            throw;
        }
    }

    public async Task<ScenarioInfo?> LoadScenarioAsync(string name)
    {
        try
        {
            var filePath = GetScenarioPath(name);
            if (!File.Exists(filePath))
            {
                _logService.Warning($"Scenario '{name}' not found");
                return null;
            }

            var json = await File.ReadAllTextAsync(filePath);
            var scenario = JsonSerializer.Deserialize<ScenarioInfo>(json);
            _logService.Info($"Scenario '{name}' loaded successfully");
            return scenario;
        }
        catch (Exception ex)
        {
            _logService.Error($"Failed to load scenario '{name}': {ex.Message}");
            return null;
        }
    }

    public async Task DeleteScenarioAsync(string name)
    {
        try
        {
            var filePath = GetScenarioPath(name);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _logService.Info($"Scenario '{name}' deleted");
            }
        }
        catch (Exception ex)
        {
            _logService.Error($"Failed to delete scenario '{name}': {ex.Message}");
            throw;
        }
    }

    private async Task RemoveProfileFromScenariosAsync(string profileName)
    {
        try
        {
            var scenarios = await GetAllScenariosAsync();
            foreach (var scenario in scenarios)
            {
                if (scenario.ProfileNames.Remove(profileName))
                {
                    await SaveScenarioAsync(scenario);
                    _logService.Info($"Removed profile '{profileName}' from scenario '{scenario.Name}'");
                }
            }
        }
        catch (Exception ex)
        {
            _logService.Error($"Failed to remove profile '{profileName}' from scenarios: {ex.Message}");
        }
    }

    private async Task RenameProfileInScenariosAsync(string oldName, string newName)
    {
        try
        {
            var scenarios = await GetAllScenariosAsync();
            foreach (var scenario in scenarios)
            {
                var idx = scenario.ProfileNames.IndexOf(oldName);
                if (idx >= 0)
                {
                    scenario.ProfileNames[idx] = newName;
                    await SaveScenarioAsync(scenario);
                    _logService.Info($"Renamed profile '{oldName}' to '{newName}' in scenario '{scenario.Name}'");
                }
            }
        }
        catch (Exception ex)
        {
            _logService.Error($"Failed to rename profile '{oldName}' in scenarios: {ex.Message}");
        }
    }

    private string GetScenarioPath(string name)
    {
        var safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_scenariosPath, $"{safeName}.json");
    }
}