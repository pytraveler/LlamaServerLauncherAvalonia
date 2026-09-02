using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Media;

namespace LlamaServerLauncher.Services;

/// <summary>
/// Scales the whole interface: every window's content is wrapped in a LayoutTransformControl,
/// which applies the scale during layout, so paddings, fixed sizes and icons grow with the text.
/// </summary>
public sealed class UiScaleService : INotifyPropertyChanged
{
    public const double MinScale = 0.8;
    public const double MaxScale = 1.6;

    public static UiScaleService Instance { get; } = new();

    private readonly Dictionary<Window, ScaledWindow> _windows = new();
    private double _scale = 1.0;

    private UiScaleService() { }

    public double Scale
    {
        get => _scale;
        set => SetScale(value, resizeWindows: true);
    }

    public string ScaleText => $"{_scale * 100:F0}%";

    /// <summary>
    /// Popups are top levels of their own and do not inherit the layout transform. Away from 1.0
    /// they have to be drawn in the window's overlay layer instead, where the transform applies.
    /// </summary>
    public bool UseOverlayPopups => Math.Abs(_scale - 1.0) > 0.001;

    public static void Install()
    {
        Control.LoadedEvent.AddClassHandler<Window>((window, _) => Instance.Attach(window));
    }

    /// <summary>Applies the stored scale at startup, leaving window sizes as they were saved.</summary>
    public void Initialize(double scale) => SetScale(scale, resizeWindows: false);

    private void SetScale(double value, bool resizeWindows)
    {
        var clamped = Math.Round(Math.Clamp(value, MinScale, MaxScale), 2);
        if (Math.Abs(clamped - _scale) < 0.001) return;

        var growth = clamped / _scale;
        _scale = clamped;

        foreach (var scaled in _windows.Values)
            ApplyTo(scaled, resizeWindows ? growth : 1.0);

        OnPropertyChanged(nameof(Scale));
        OnPropertyChanged(nameof(ScaleText));
        OnPropertyChanged(nameof(UseOverlayPopups));
    }

    private void Attach(Window window)
    {
        if (_windows.ContainsKey(window)) return;
        if (window.Content is not Control content || content is LayoutTransformControl) return;

        var root = new LayoutTransformControl();
        var scaled = new ScaledWindow(window, root, window.MinWidth, window.MinHeight);
        // Registered before the swap: re-parenting reloads the tree, which raises Loaded again.
        _windows[window] = scaled;
        window.Closed += (_, _) => _windows.Remove(window);

        // The content has to leave the window before it can be given a new parent.
        window.Content = null;
        root.Child = content;
        window.Content = root;

        ApplyTo(scaled, 1.0);
    }

    private void ApplyTo(ScaledWindow scaled, double growth)
    {
        // A fresh transform rather than a mutated one, so the control always sees the change.
        scaled.Root.LayoutTransform = new ScaleTransform(_scale, _scale);

        var window = scaled.Window;
        if (scaled.MinWidth > 0) window.MinWidth = scaled.MinWidth * _scale;
        if (scaled.MinHeight > 0) window.MinHeight = scaled.MinHeight * _scale;

        if (Math.Abs(growth - 1.0) < 0.001) return;
        if (window.WindowState != WindowState.Normal || window.SizeToContent != SizeToContent.Manual) return;

        var (maxWidth, maxHeight) = WorkingAreaSize(window);
        window.Width = Math.Min(window.Bounds.Width * growth, maxWidth);
        window.Height = Math.Min(window.Bounds.Height * growth, maxHeight);
    }

    private static (double Width, double Height) WorkingAreaSize(Window window)
    {
        try
        {
            var screen = window.Screens?.ScreenFromWindow(window);
            if (screen != null)
            {
                var scaling = screen.Scaling > 0 ? screen.Scaling : 1.0;
                return (screen.WorkingArea.Width / scaling, screen.WorkingArea.Height / scaling);
            }
        }
        catch { }

        return (double.MaxValue, double.MaxValue);
    }

    private sealed record ScaledWindow(Window Window, LayoutTransformControl Root, double MinWidth, double MinHeight);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
