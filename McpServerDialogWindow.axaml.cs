using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LlamaServerLauncher.Models;
using LlamaServerLauncher.Services;
using LlamaServerLauncher.ViewModels;

namespace LlamaServerLauncher;

public partial class McpServerDialogWindow : Window
{
    private McpServerDialogViewModel? _viewModel;

    public DialogGeometry? CapturedGeometry { get; private set; }

    public bool IsConfirmed { get; private set; }

    public bool IsDeleteRequested { get; private set; }

    public McpServerDialogWindow()
    {
        InitializeComponent();
    }

    public void SetViewModel(McpServerDialogViewModel vm, Dictionary<string, DialogGeometry>? dialogGeometryDict = null)
    {
        _viewModel = vm;
        DataContext = vm;
        vm.RequestClose += Close;
        if (dialogGeometryDict != null)
            DialogPositionHelper.ApplySavedGeometry(this, dialogGeometryDict, "McpServerDialog");
    }

    private async void BrowseCommandClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            await _viewModel.BrowseCommandAsync();
    }

    private async void BrowseWorkingDirectoryClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            await _viewModel.BrowseWorkingDirectoryAsync();
    }

    private async void TestClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            await _viewModel.ProbeAsync();
    }

    private void DeleteClick(object? sender, RoutedEventArgs e)
    {
        IsDeleteRequested = true;
        IsConfirmed = false;
        Close();
    }

    private void SaveClick(object? sender, RoutedEventArgs e)
    {
        IsConfirmed = true;
        Close();
    }

    private void CancelClick(object? sender, RoutedEventArgs e)
    {
        IsConfirmed = false;
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CancelClick(null, e);
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
        base.OnClosing(e);
    }
}
