using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using LlamaServerLauncher.Resources;
using System;

namespace LlamaServerLauncher.Controls;

public partial class TriStateSelector : UserControl
{
    public static readonly StyledProperty<bool?> ValueProperty =
        AvaloniaProperty.Register<TriStateSelector, bool?>(nameof(Value), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<TriStateSelector, string?>(nameof(Label));

    public bool? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public TriStateSelector()
    {
        InitializeComponent();
        UpdateVisualState();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ValueProperty)
        {
            UpdateVisualState();
        }
        else if (change.Property == LabelProperty)
        {
            var text = change.GetNewValue<string?>();
            LabelText.Text = text;
            AutomationProperties.SetName(Segments, text);
        }
    }

    private void OnSegmentClick(object? sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, OffButton)) Value = false;
        else if (ReferenceEquals(sender, AutoButton)) Value = null;
        else if (ReferenceEquals(sender, OnButton)) Value = true;

        UpdateVisualState();
    }

    private void UpdateVisualState()
    {
        OffButton.IsChecked = Value == false;
        AutoButton.IsChecked = Value == null;
        OnButton.IsChecked = Value == true;
    }
}
