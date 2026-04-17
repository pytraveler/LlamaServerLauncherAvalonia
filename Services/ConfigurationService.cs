using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using LlamaServerLauncher.Models;

namespace LlamaServerLauncher.Services;

public class ConfigurationService
{
    private readonly string _profilesPath;
    private readonly string _appSettingsPath;
    private readonly LogService _logService;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public ConfigurationService(LogService logService)
    {
        _logService = logService;
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LlamaServerLauncherAvalonia"
        );
        _profilesPath = Path.Combine(appDataPath, "profiles");
        _appSettingsPath = Path.Combine(appDataPath, "app.json");
        Directory.CreateDirectory(_profilesPath);
    }

    public async Task SaveAppSettingsAsync(AppSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            await File.WriteAllTextAsync(_appSettingsPath, json);
            _logService.Info("Application settings saved successfully");
        }
        catch (Exception ex)
        {
            _logService.Error($"Failed to save application settings: {ex.Message}");
        }
    }

    public async Task<AppSettings> LoadAppSettingsAsync()
    {
        try
        {
            if (!File.Exists(_appSettingsPath))
            {
                return new AppSettings();
            }

            var json = await File.ReadAllTextAsync(_appSettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json);
            _logService.Info("Application settings loaded successfully");
            return settings ?? new AppSettings();
        }
        catch (Exception ex)
        {
            _logService.Error($"Failed to load application settings: {ex.Message}");
            return new AppSettings();
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

            var json = JsonSerializer.Serialize(profile, JsonOptions);
            var filePath = GetProfilePath(name);
            await File.WriteAllTextAsync(filePath, json);
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
                catch
                {
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
                
                var newJson = JsonSerializer.Serialize(profile, JsonOptions);
                await File.WriteAllTextAsync(newFilePath, newJson);
                File.Delete(oldFilePath);
                _logService.Info($"Profile renamed from '{oldName}' to '{newName}'");
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

                    var newJson = JsonSerializer.Serialize(profile, JsonOptions);
                    await File.WriteAllTextAsync(profile.FilePath, newJson);
                    
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
}