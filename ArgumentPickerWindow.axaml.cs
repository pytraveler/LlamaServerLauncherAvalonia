using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LlamaServerLauncher.ViewModels;

namespace LlamaServerLauncher;

public partial class ArgumentPickerWindow : Window
{
    private ArgumentPickerViewModel? _viewModel;

    public ArgumentPickerWindow()
    {
        InitializeComponent();
    }

    public void SetViewModel(ArgumentPickerViewModel vm)
    {
        _viewModel = vm;
        DataContext = vm;
        vm.RequestClose += () => Close();
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
}
