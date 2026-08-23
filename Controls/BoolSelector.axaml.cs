using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using LlamaServerLauncher.Resources;

namespace LlamaServerLauncher.Controls;

public partial class BoolSelector : UserControl
{
    public static readonly StyledProperty<bool> ValueProperty =
        AvaloniaProperty.Register<BoolSelector, bool>(nameof(Value), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<BoolSelector, string?>(nameof(Label));

    public bool Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public BoolSelector()
    {
        InitializeComponent();
        ApplyCaptions();
        UpdateVisualState();

        AttachedToVisualTree += (_, _) => LocalizedStrings.CultureChanged += ApplyCaptions;
        DetachedFromVisualTree += (_, _) => LocalizedStrings.CultureChanged -= ApplyCaptions;

        Tapped += OnTapped;
    }

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;
    }

    private void ApplyCaptions()
    {
        OffButton.Content = LocalizedStrings.Instance.TriStateOff;
        OnButton.Content = LocalizedStrings.Instance.TriStateOn;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ValueProperty)
            UpdateVisualState();
        else if (change.Property == LabelProperty)
            AutomationProperties.SetName(Segments, change.GetNewValue<string?>());
    }

    private void OnSegmentClick(object? sender, RoutedEventArgs e)
    {
        Value = ReferenceEquals(sender, OnButton);
        UpdateVisualState();
        e.Handled = true;
    }

    private void UpdateVisualState()
    {
        OffButton.IsChecked = !Value;
        OnButton.IsChecked = Value;
    }
}
