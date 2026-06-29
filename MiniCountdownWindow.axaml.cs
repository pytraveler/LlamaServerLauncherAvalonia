using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LlamaServerLauncher.Models;
using LlamaServerLauncher.Resources;
using LlamaServerLauncher.Services;
using LlamaServerLauncher.ViewModels;

namespace LlamaServerLauncher;

public partial class MiniCountdownWindow : Window
{
    private MiniCountdownViewModel? _viewModel;

    public LocalizedStrings Localized => LocalizedStrings.Instance;

    public DialogGeometry? CapturedGeometry { get; private set; }

    public MiniCountdownViewModel? ViewModel => _viewModel;

    public MiniCountdownWindow()
    {
        InitializeComponent();
    }

    public void SetViewModel(MiniCountdownViewModel viewModel, ConfigurationService configService, Dictionary<string, DialogGeometry>? dialogGeometryDict = null)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        if (dialogGeometryDict != null)
            DialogPositionHelper.ApplySavedGeometry(this, dialogGeometryDict, "MiniCountdown");
    }

    private void PrimaryClick(object? sender, RoutedEventArgs e)
    {
        _viewModel?.PrimaryCommand.Execute(null);
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
        base.OnClosing(e);
    }
}
