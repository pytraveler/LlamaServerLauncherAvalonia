using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LlamaServerLauncher.Models;
using LlamaServerLauncher.Resources;
using LlamaServerLauncher.Services;
using LlamaServerLauncher.ViewModels;

namespace LlamaServerLauncher;

public partial class ScenarioDialogWindow : Window
{
    private ScenarioDialogViewModel? _viewModel;
    private ConfigurationService? _configService;
    private Dictionary<string, DialogGeometry>? _dialogGeometryDict;

    /// <summary>Geometry captured in OnClosing for the caller to save asynchronously.</summary>
    public DialogGeometry? CapturedGeometry { get; private set; }

    public ScenarioDialogWindow()
    {
        InitializeComponent();
    }

    public void SetViewModel(ScenarioDialogViewModel viewModel, ConfigurationService configService, Dictionary<string, DialogGeometry>? dialogGeometryDict = null)
    {
        _viewModel = viewModel;
        _configService = configService;
        _dialogGeometryDict = dialogGeometryDict;
        DataContext = _viewModel;
        _viewModel.RequestClose += OnRequestClose;
        if (dialogGeometryDict != null)
            DialogPositionHelper.ApplySavedGeometry(this, dialogGeometryDict, "ScenarioDialog");
    }

    private async void HelpClick(object? sender, RoutedEventArgs e)
    {
        await HelpService.ShowAsync(this, HelpService.TopicScenarios,
            LocalizedStrings.Instance.HelpTitleScenarios, _dialogGeometryDict);
    }

    protected override void OnKeyDown(Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.F1 && e.KeyModifiers == Avalonia.Input.KeyModifiers.None)
        {
            HelpClick(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    private void OnRequestClose()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => Close());
    }

    private void AddProfileClick(object? sender, RoutedEventArgs e)
    {
        _viewModel?.AddProfile();
    }

    private void RemoveProfileClick(object? sender, RoutedEventArgs e)
    {
        _viewModel?.RemoveProfile();
    }

    private async void CloneToScenarioClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            await _viewModel.CloneToScenarioAsync(this);
    }

    private void MoveUpClick(object? sender, RoutedEventArgs e)
    {
        _viewModel?.MoveUp();
    }

    private void MoveDownClick(object? sender, RoutedEventArgs e)
    {
        _viewModel?.MoveDown();
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
        if (_viewModel != null)
        {
            _viewModel.RequestClose -= OnRequestClose;
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
