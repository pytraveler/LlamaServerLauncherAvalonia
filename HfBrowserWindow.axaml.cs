using System.Collections.Generic;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LlamaServerLauncher.Models;
using LlamaServerLauncher.Services;
using LlamaServerLauncher.ViewModels;

namespace LlamaServerLauncher;

public partial class HfBrowserWindow : Window
{
    private HfBrowserViewModel? _viewModel;

    public DialogGeometry? CapturedGeometry { get; private set; }

    public bool IsConfirmed { get; private set; }

    public string? SelectedPath => _viewModel?.ConfirmedPath;

    public string TargetFolder => _viewModel?.TargetFolder ?? "";

    public string LastQuery => _viewModel?.QueryText ?? "";

    public string Token => _viewModel?.Token ?? "";

    public bool SubfolderPerRepo => _viewModel?.SubfolderPerRepo ?? false;

    public HfBrowserWindow()
    {
        InitializeComponent();
    }

    public void SetViewModel(HfBrowserViewModel vm, Dictionary<string, DialogGeometry>? dialogGeometryDict = null)
    {
        _viewModel = vm;
        DataContext = vm;
        vm.RequestClose += OnRequestClose;
        if (dialogGeometryDict != null)
            DialogPositionHelper.ApplySavedGeometry(this, dialogGeometryDict, "HfBrowser");
    }

    private void OnRequestClose()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => Close());
    }

    private async void GoClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            await _viewModel.GoAsync();
    }

    private async void QueryKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (_viewModel != null)
            await _viewModel.GoAsync();
    }

    private async void BrowseFolderClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            await _viewModel.BrowseFolderAsync();
    }

    private async void DownloadClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            await _viewModel.DownloadAsync();
    }

    private async void QuantDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_viewModel != null && _viewModel.CanDownload)
            await _viewModel.DownloadAsync();
    }

    private void StopClick(object? sender, RoutedEventArgs e)
    {
        _viewModel?.CancelDownload();
    }

    private void OpenPageClick(object? sender, RoutedEventArgs e)
    {
        var url = _viewModel?.RepoPageUrl;
        if (string.IsNullOrEmpty(url)) return;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { }
    }

    private void UseClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null && _viewModel.Confirm())
            IsConfirmed = true;
    }

    private void CloseClick(object? sender, RoutedEventArgs e)
    {
        _viewModel?.Cancel();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.RequestClose -= OnRequestClose;
            _viewModel.CancelDownload();
        }
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
