using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LlamaServerLauncher.Models;
using LlamaServerLauncher.Services;
using LlamaServerLauncher.ViewModels;

namespace LlamaServerLauncher;

public partial class BenchmarkLaunchWindow : Window
{
    private BenchmarkLaunchViewModel? _viewModel;

    public DialogGeometry? CapturedGeometry { get; private set; }

    public BenchmarkLaunchWindow()
    {
        InitializeComponent();
    }

    public void SetViewModel(BenchmarkLaunchViewModel viewModel, Dictionary<string, DialogGeometry>? dialogGeometryDict = null)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.RequestClose += OnRequestClose;
        if (dialogGeometryDict != null)
            DialogPositionHelper.ApplySavedGeometry(this, dialogGeometryDict, "BenchmarkLaunch");
    }

    private void OnRequestClose()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(Close);
    }

    private void RunClick(object? sender, RoutedEventArgs e) => _viewModel?.Run();

    private void CancelClick(object? sender, RoutedEventArgs e) => _viewModel?.Cancel();

    private void ArgToggleTapped(object? sender, RoutedEventArgs e) => _viewModel?.OnToggleChanged();

    private void ArgToggleRightTapped(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: CustomArgumentItem item })
            _viewModel?.RemoveArgItem(item);
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
