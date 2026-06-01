using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LlamaServerLauncher.Resources;
using LlamaServerLauncher.ViewModels;

namespace LlamaServerLauncher;

public partial class DownloadDialogWindow : Window
{
    private DownloadDialogViewModel? _viewModel;

    public LocalizedStrings Localized => LocalizedStrings.Instance;

    public bool DownloadCompleted { get; private set; }

    public DownloadDialogWindow()
    {
        InitializeComponent();
    }

    public void SetViewModel(DownloadDialogViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = _viewModel;
        _viewModel.RequestClose += OnRequestClose;
    }

    private void OnRequestClose()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            DownloadCompleted = !string.IsNullOrEmpty(_viewModel?.StatusMessage) &&
                                _viewModel.StatusMessage == LocalizedStrings.GetString("DownloadComplete");
            Close();
        });
    }

    private async void DownloadClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            await _viewModel.DownloadAsync();
    }

    private async void DownloadToFolderClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            await _viewModel.DownloadToFolderAsync();
    }

    private void CancelClick(object? sender, RoutedEventArgs e)
    {
        _viewModel?.Close();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _viewModel?.CancelDownload();
        base.OnClosing(e);
    }
}
