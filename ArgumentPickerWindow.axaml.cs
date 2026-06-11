using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LlamaServerLauncher.Models;
using LlamaServerLauncher.Services;
using LlamaServerLauncher.ViewModels;

namespace LlamaServerLauncher;

public partial class ArgumentPickerWindow : Window
{
    private ArgumentPickerViewModel? _viewModel;
    private ConfigurationService? _configService;

    /// <summary>Geometry captured in OnClosing for the caller to save asynchronously.</summary>
    public DialogGeometry? CapturedGeometry { get; private set; }

    public ArgumentPickerWindow()
    {
        InitializeComponent();
    }

    public void SetViewModel(ArgumentPickerViewModel vm, ConfigurationService configService, Dictionary<string, DialogGeometry>? dialogGeometryDict = null)
    {
        _viewModel = vm;
        _configService = configService;
        DataContext = vm;
        vm.RequestClose += () => Close();
        if (dialogGeometryDict != null)
            DialogPositionHelper.ApplySavedGeometry(this, dialogGeometryDict, "ArgumentPicker");
    }

    public bool IsConfirmed { get; private set; }

    public List<HelpArgumentItem>? SelectedArguments =>
        _viewModel?.GetSelectedItems();

    private void AddClick(object? sender, RoutedEventArgs e)
    {
        IsConfirmed = true;
        Close();
    }

    private void CancelClick(object? sender, RoutedEventArgs e)
    {
        IsConfirmed = false;
        Close();
    }

    private void ItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control control && control.DataContext is HelpArgumentItem item)
        {
            item.IsSelected = !item.IsSelected;
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddClick(null, e);
            e.Handled = true;
            return;
        }

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
