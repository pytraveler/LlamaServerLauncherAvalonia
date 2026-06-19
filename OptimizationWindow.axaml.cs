using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using LlamaServerLauncher.Models;
using LlamaServerLauncher.Resources;
using LlamaServerLauncher.Services;
using LlamaServerLauncher.ViewModels;

namespace LlamaServerLauncher;

public partial class OptimizationWindow : Window
{
    private OptimizationViewModel? _viewModel;
    private Dictionary<string, DialogGeometry>? _dialogGeometryDict;

    public DialogGeometry? CapturedGeometry { get; private set; }

    public OptimizationWindow()
    {
        InitializeComponent();
    }

    public void SetViewModel(OptimizationViewModel vm, Dictionary<string, DialogGeometry>? dialogGeometryDict = null)
    {
        _viewModel = vm;
        _dialogGeometryDict = dialogGeometryDict;
        DataContext = vm;
        vm.RequestClose += Close;
        if (dialogGeometryDict != null)
            DialogPositionHelper.ApplySavedGeometry(this, dialogGeometryDict, "Optimization");
    }

    private async void OpenGuideClick(object? sender, RoutedEventArgs e)
    {
        await MarkdownViewerWindow.ShowAsync(this, LoadGuide(), LocalizedStrings.Instance.OptGuideTitle, _dialogGeometryDict);
    }

    private static string LoadGuide()
    {
        bool ru = LocalizedStrings.CurrentCulture.TwoLetterISOLanguageName == "ru";
        var uri = new Uri($"avares://LlamaServerLauncher/Resources/Docs/optimization-guide.{(ru ? "ru" : "en")}.md");
        try
        {
            using var stream = AssetLoader.Open(uri);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            return $"```\n{ex.Message}\n```";
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        CapturedGeometry = new DialogGeometry
        {
            Width = Width,
            Height = Height,
            Left = Position.X,
            Top = Position.Y
        };

        _viewModel?.Cancel();

        base.OnClosing(e);
    }
}
