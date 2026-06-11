using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using LlamaServerLauncher.Models;
using LlamaServerLauncher.ViewModels;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace LlamaServerLauncher.Controls;

public partial class HistoryTextBox : UserControl
{
    public static readonly StyledProperty<string?> PropertyNameProperty =
        AvaloniaProperty.Register<HistoryTextBox, string?>(nameof(PropertyName));

    public static readonly StyledProperty<string?> PlaceholderTextProperty =
        AvaloniaProperty.Register<HistoryTextBox, string?>(nameof(PlaceholderText));

    public string? PropertyName
    {
        get => GetValue(PropertyNameProperty);
        set => SetValue(PropertyNameProperty, value);
    }

    public string? PlaceholderText
    {
        get => GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    private IDisposable? _textBinding;
    private ArgType? _filterType;
    private string _lastValidText = "";
    private bool _suppressValidation;

    public HistoryTextBox()
    {
        InitializeComponent();
        InnerTextBox.TextChanged += OnInnerTextChanged;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        ApplyTextBinding();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == PropertyNameProperty)
            ApplyTextBinding();
        else if (change.Property == PlaceholderTextProperty)
            InnerTextBox.PlaceholderText = change.GetNewValue<string?>();
        else if (change.Property == IsEnabledProperty)
            InnerTextBox.IsEnabled = change.GetNewValue<bool>();
    }

    private void ApplyTextBinding()
    {
        _textBinding?.Dispose();
        _textBinding = null;

        // Determine the input filter from the argument type of the bound property.
        _filterType = PropertyName != null ? ServerConfiguration.GetArgType(PropertyName) : null;
        _lastValidText = InnerTextBox.Text ?? "";

        if (PropertyName != null && DataContext != null)
        {
            _textBinding = InnerTextBox.Bind(TextBox.TextProperty,
                new Binding(PropertyName) { Mode = BindingMode.TwoWay });
        }
    }

    private void OnInnerTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppressValidation) return;
        // Only Int/Double arguments are filtered; everything else accepts free text.
        if (_filterType != ArgType.Int && _filterType != ArgType.Double) return;

        var text = InnerTextBox.Text ?? "";
        if (IsValidNumeric(text, _filterType.Value))
        {
            _lastValidText = text;
            return;
        }

        // Reject the change: restore the last valid text and keep the caret sensible.
        var caret = InnerTextBox.CaretIndex;
        var removed = text.Length - _lastValidText.Length;
        _suppressValidation = true;
        InnerTextBox.Text = _lastValidText;
        InnerTextBox.CaretIndex = Math.Clamp(caret - (removed > 0 ? removed : 0), 0, _lastValidText.Length);
        _suppressValidation = false;
    }

    private static bool IsValidNumeric(string text, ArgType type)
    {
        if (text.Length == 0) return true; // empty means "not set"
        // Allow partial input while typing: an optional leading minus, digits, and
        // (for Double) a single decimal point. Final validity is enforced by parsing.
        return type == ArgType.Int
            ? Regex.IsMatch(text, @"^-?\d*$")
            : Regex.IsMatch(text, @"^-?\d*\.?\d*$");
    }

    private void HistoryButton_Click(object? sender, RoutedEventArgs e)
    {
        // Try multiple strategies to find the ViewModel
        var vm = this.DataContext as MainViewModel;
        if (vm == null)
            vm = (TopLevel.GetTopLevel(this) as Window)?.DataContext as MainViewModel;
        if (vm == null)
        {
            // Walk up the visual tree
            var parent = this.GetVisualParent();
            while (parent != null)
            {
                if (parent is Window w && w.DataContext is MainViewModel mvm)
                { vm = mvm; break; }
                parent = parent.GetVisualParent();
            }
        }
        if (vm == null) return;

        var values = vm.GetRecentValues(PropertyName ?? "");
        if (values == null || values.Count == 0) return;

        var flyout = new MenuFlyout();
        foreach (var v in values)
        {
            var val = v;
            var item = new MenuItem { Header = val };
            item.Click += (_, _) => InnerTextBox.Text = val;
            flyout.Items.Add(item);
        }

        flyout.ShowAt((Control)sender!);
    }
}
