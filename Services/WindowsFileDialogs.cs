using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace LlamaServerLauncher.Services;

public static class WindowsFileDialogs
{
    public static IntPtr OwnerHandle => LlamaServerLauncher.MainWindow.Instance?.WindowHandle ?? IntPtr.Zero;

    private static Window? MainWindow => LlamaServerLauncher.MainWindow.Instance;

    public static async Task<string[]?> OpenFileDialogAsync(string title, (string Name, string Ext)[]? filters = null, bool allowMultiple = false)
    {
        if (MainWindow == null) return null;

        var storageProvider = MainWindow.StorageProvider;

        var filePickerOptions = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = allowMultiple
        };

        if (filters != null && filters.Length > 0)
        {
            filePickerOptions.FileTypeFilter = filters.Select(f => 
                new FilePickerFileType(f.Name) { Patterns = new[] { f.Ext } }
            ).ToList();
        }

        var result = await storageProvider.OpenFilePickerAsync(filePickerOptions);

        if (result.Count == 0)
            return null;

        var paths = new string[result.Count];
        for (int i = 0; i < result.Count; i++)
        {
            paths[i] = result[i].Path.LocalPath;
        }
        return paths;
    }

    public static async Task<string?> SaveFileDialogAsync(string title, string defaultExtension = "", (string Name, string Ext)[]? filters = null)
    {
        if (MainWindow == null) return null;

        var storageProvider = MainWindow.StorageProvider;

        var filePickerOptions = new FilePickerSaveOptions
        {
            Title = title,
            ShowOverwritePrompt = true
        };

        if (!string.IsNullOrEmpty(defaultExtension))
        {
            filePickerOptions.DefaultExtension = defaultExtension;
        }

        if (filters != null && filters.Length > 0)
        {
            filePickerOptions.FileTypeChoices = filters.Select(f => 
                new FilePickerFileType(f.Name) { Patterns = new[] { f.Ext } }
            ).ToList();
        }

        var result = await storageProvider.SaveFilePickerAsync(filePickerOptions);

        return result?.Path.LocalPath;
    }

    public static async Task<string?> OpenFolderDialogAsync(string title)
    {
        if (MainWindow == null) return null;

        var storageProvider = MainWindow.StorageProvider;

        var folderPickerOptions = new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        };

        var result = await storageProvider.OpenFolderPickerAsync(folderPickerOptions);

        if (result.Count == 0)
            return null;

        return result[0].Path.LocalPath;
    }

    // Sync versions - these are not well supported in Avalonia async model
    public static string[]? OpenFileDialog(string title, (string Name, string Ext)[]? filters = null, bool allowMultiple = false)
    {
        return null;
    }

    public static string? SaveFileDialog(string title, string defaultExtension = "", (string Name, string Ext)[]? filters = null)
    {
        return null;
    }

    public static string? OpenFolderDialog(string title)
    {
        return null;
    }
}
