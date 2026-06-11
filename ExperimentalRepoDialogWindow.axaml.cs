using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LlamaServerLauncher.Models;
using LlamaServerLauncher.Resources;
using LlamaServerLauncher.Services;
using LlamaServerLauncher.ViewModels;

namespace LlamaServerLauncher;

public partial class ExperimentalRepoDialogWindow : Window
{
    private ExperimentalRepoDialogViewModel? _viewModel;

    public LocalizedStrings Localized => LocalizedStrings.Instance;

    public DialogGeometry? CapturedGeometry { get; private set; }

    public ExperimentalRepoDialogWindow()
    {
        InitializeComponent();
    }

    public void SetViewModel(ExperimentalRepoDialogViewModel viewModel, Dictionary<string, DialogGeometry>? dialogGeometryDict = null)
    {
        _viewModel = viewModel;
        DataContext = _viewModel;
        _viewModel.RequestClose += OnRequestClose;
        if (dialogGeometryDict != null)
            DialogPositionHelper.ApplySavedGeometry(this, dialogGeometryDict, "ExperimentalRepoDialog");
    }

    private void OnRequestClose()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => Close());
    }

    private void OkClick(object? sender, RoutedEventArgs e)
    {
        _viewModel?.Save();
    }

    private void CancelClick(object? sender, RoutedEventArgs e)
    {
        _viewModel?.Cancel();
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
