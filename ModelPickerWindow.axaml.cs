using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LlamaServerLauncher.Models;
using LlamaServerLauncher.Services;
using LlamaServerLauncher.ViewModels;

namespace LlamaServerLauncher;

public partial class ModelPickerWindow : Window
{
    private ModelPickerViewModel? _viewModel;

    public DialogGeometry? CapturedGeometry { get; private set; }

    public bool IsConfirmed { get; private set; }

    public string? SelectedPath => _viewModel?.ConfirmedPath;

    public string ScannedFolder => _viewModel?.FolderPath ?? "";

    public bool ScannedRecursive => _viewModel?.Recursive ?? false;

    public ModelPickerWindow()
    {
        InitializeComponent();
    }

    public void SetViewModel(ModelPickerViewModel vm, Dictionary<string, DialogGeometry>? dialogGeometryDict = null)
    {
        _viewModel = vm;
        DataContext = vm;
        vm.RequestClose += OnRequestClose;
        if (dialogGeometryDict != null)
            DialogPositionHelper.ApplySavedGeometry(this, dialogGeometryDict, "ModelPicker");
    }

    private void OnRequestClose()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => Close());
    }

    protected override void OnOpened(System.EventArgs e)
    {
        base.OnOpened(e);
        if (_viewModel != null)
            _ = _viewModel.RescanAsync();
    }

    private async void BrowseClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            await _viewModel.BrowseFolderAsync();
    }

    private async void RescanClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            await _viewModel.RescanAsync();
    }

    private async void RecursiveClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            await _viewModel.RescanAsync();
    }

    private void ListDoubleTapped(object? sender, TappedEventArgs e)
    {
        Confirm();
    }

    private void SelectClick(object? sender, RoutedEventArgs e)
    {
        Confirm();
    }

    private void CancelClick(object? sender, RoutedEventArgs e)
    {
        _viewModel?.Cancel();
    }

    private void Confirm()
    {
        if (_viewModel != null && _viewModel.Confirm())
            IsConfirmed = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Confirm();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape)
        {
            _viewModel?.Cancel();
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.RequestClose -= OnRequestClose;
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
