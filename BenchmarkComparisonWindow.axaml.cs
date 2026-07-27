using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LlamaServerLauncher.Models;
using LlamaServerLauncher.Resources;
using LlamaServerLauncher.Services;
using LlamaServerLauncher.ViewModels;

namespace LlamaServerLauncher;

public partial class BenchmarkComparisonWindow : Window
{
    private BenchmarkComparisonViewModel? _viewModel;
    private Dictionary<string, DialogGeometry>? _dialogGeometryDict;

    public DialogGeometry? CapturedGeometry { get; private set; }

    public BenchmarkComparisonWindow()
    {
        InitializeComponent();
    }

    public void SetViewModel(BenchmarkComparisonViewModel vm, Dictionary<string, DialogGeometry>? dialogGeometryDict = null)
    {
        _viewModel = vm;
        _dialogGeometryDict = dialogGeometryDict;
        DataContext = vm;
        vm.RequestClose += Close;
        Closed += (_, _) => vm.Detach();
        if (dialogGeometryDict != null)
            DialogPositionHelper.ApplySavedGeometry(this, dialogGeometryDict, "BenchmarkComparison");
    }

    private void RefreshClick(object? sender, RoutedEventArgs e) => _viewModel?.Refresh();

    private void RunBenchmarkClick(object? sender, RoutedEventArgs e) => _viewModel?.RunBenchmark();

    private async void HelpClick(object? sender, RoutedEventArgs e)
    {
        await HelpService.ShowAsync(this, HelpService.TopicBenchmarks,
            LocalizedStrings.Instance.HelpTitleBenchmarks, _dialogGeometryDict);
    }

    private void ProfileFilterAllClick(object? sender, RoutedEventArgs e) => _viewModel?.SelectAllProfiles();

    private void MetricFilterAllClick(object? sender, RoutedEventArgs e) => _viewModel?.SelectAllMetrics();

    private void MetricFilterNoneClick(object? sender, RoutedEventArgs e) => _viewModel?.ClearMetrics();

    private async void ExportMdClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            await _viewModel.ExportMarkdownAsync();
    }

    private async void ExportZipClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            await _viewModel.ExportZipAsync();
    }

    private void RevealRunClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: BenchmarkRunRow row })
            _viewModel?.RevealRun(row);
    }

    private async void PinFilesClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null && sender is Control { DataContext: BenchmarkRunRow row })
            await _viewModel.PinFilesAsync(row);
    }

    private async void PinFolderClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null && sender is Control { DataContext: BenchmarkRunRow row })
            await _viewModel.PinFolderAsync(row);
    }

    private async void DeleteRunClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null)
            return;
        var result = await MessageBox.ShowAsync(
            this,
            LocalizedStrings.Instance.BenchmarkDeleteRunConfirm,
            LocalizedStrings.Instance.BenchmarkDeleteRun,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (result == MessageBoxResult.Yes)
            _viewModel.DeleteSelectedRuns();
    }

    private async void SaveSetClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            await _viewModel.SaveSetAsync();
    }

    private async void DeleteSetClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            await _viewModel.DeleteSetAsync();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.F1 && e.KeyModifiers == KeyModifiers.None)
        {
            HelpClick(this, new RoutedEventArgs());
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
