using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LlamaServerLauncher.Models;
using LlamaServerLauncher.Resources;
using LlamaServerLauncher.Services;
using LlamaServerLauncher.ViewModels;

namespace LlamaServerLauncher;

public partial class DownloadDialogWindow : Window
{
    private DownloadDialogViewModel? _viewModel;
    private ConfigurationService? _configService;

    public LocalizedStrings Localized => LocalizedStrings.Instance;

    public bool DownloadCompleted { get; private set; }

    public DialogGeometry? CapturedGeometry { get; private set; }

    public DownloadDialogWindow()
    {
        InitializeComponent();
    }

    public void SetViewModel(DownloadDialogViewModel viewModel, ConfigurationService configService, Dictionary<string, DialogGeometry>? dialogGeometryDict = null)
    {
        _viewModel = viewModel;
        _configService = configService;
        DataContext = _viewModel;
        _viewModel.RequestClose += OnRequestClose;
        if (dialogGeometryDict != null)
            DialogPositionHelper.ApplySavedGeometry(this, dialogGeometryDict, "DownloadDialog");
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

    private async void DownloadExperimentalClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            await _viewModel.DownloadExperimentalAsync();
    }

    private void CancelClick(object? sender, RoutedEventArgs e)
    {
        _viewModel?.Close();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _viewModel?.CancelDownload();
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
