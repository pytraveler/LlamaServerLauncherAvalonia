using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LlamaServerLauncher.Controls;
using LlamaServerLauncher.Models;
using LlamaServerLauncher.Resources;
using LlamaServerLauncher.Services;

namespace LlamaServerLauncher;

public partial class MarkdownViewerWindow : Window, INotifyPropertyChanged
{
    public const string GeometryKey = "MarkdownViewer";

    public LocalizedStrings Localized => LocalizedStrings.Instance;

    public DialogGeometry? CapturedGeometry { get; private set; }

    private Action? _onClosed;

    private string _windowTitle = LocalizedStrings.Instance.MarkdownViewerTitle;
    public string WindowTitle
    {
        get => _windowTitle;
        private set
        {
            if (_windowTitle == value) return;
            _windowTitle = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WindowTitle)));
        }
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    public MarkdownViewerWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    public void SetContent(string markdown, string? title = null)
    {
        if (!string.IsNullOrWhiteSpace(title))
            WindowTitle = title!;
        ContentHost.Content = MarkdownRenderer.Render(markdown);
    }

    public static Task ShowAsync(Window owner, string markdown, string? title = null,
        Dictionary<string, DialogGeometry>? geometryDict = null, Action? onClosed = null)
    {
        var dialog = new MarkdownViewerWindow();
        dialog.SetContent(markdown, title);
        return ShowCore(dialog, owner, geometryDict, onClosed);
    }

    public static Task ShowFileAsync(Window owner, string filePath, string? title = null,
        Dictionary<string, DialogGeometry>? geometryDict = null, Action? onClosed = null)
    {
        var dialog = new MarkdownViewerWindow();
        string content;
        try
        {
            content = File.ReadAllText(filePath);
        }
        catch (Exception ex)
        {
            content = $"```\n{ex.Message}\n```";
        }
        dialog.SetContent(content, title ?? Path.GetFileName(filePath));
        return ShowCore(dialog, owner, geometryDict, onClosed);
    }

    private static async Task ShowCore(MarkdownViewerWindow dialog, Window owner,
        Dictionary<string, DialogGeometry>? geometryDict, Action? onClosed)
    {
        dialog._onClosed = onClosed;
        if (geometryDict != null)
            DialogPositionHelper.ApplySavedGeometry(dialog, geometryDict, GeometryKey);

        await dialog.ShowDialog(owner);

        if (geometryDict != null && dialog.CapturedGeometry != null)
            geometryDict[GeometryKey] = dialog.CapturedGeometry;
    }

    private void CloseClick(object? sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        CapturedGeometry = new DialogGeometry
        {
            Width = Width,
            Height = Height,
            Left = Position.X,
            Top = Position.Y
        };
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _onClosed?.Invoke();
    }
}
