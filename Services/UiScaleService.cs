using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
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
    private bool _autoSizeWindows;
    private bool _resizing;

    private UiScaleService() { }

    public double Scale
    {
        get => _scale;
        set => SetScale(value, resizeWindows: true);
    }

    public bool AutoSizeWindows
    {
        get => _autoSizeWindows;
        set
        {
            if (_autoSizeWindows == value) return;
            _autoSizeWindows = value;

            foreach (var scaled in _windows.Values)
            {
                ApplyMinSizes(scaled);
                if (value) ResizeToScale(scaled);
            }

            OnPropertyChanged();
        }
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

        _scale = clamped;

        foreach (var scaled in _windows.Values)
        {
            ApplyTransform(scaled);
            ApplyMinSizes(scaled);

            if (resizeWindows)
                ResizeToScale(scaled);
            else
                CaptureBaseSize(scaled);   
        }

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

        ApplyTransform(scaled);
        ApplyMinSizes(scaled);

        if (_autoSizeWindows && Math.Abs(_scale - 1.0) > 0.001 && !IsApplicationMainWindow(window))
            GrowToContent(scaled);

        CaptureBaseSize(scaled);

        window.PropertyChanged += (_, e) =>
        {
            if (e.Property != Visual.BoundsProperty || _resizing) return;
            if (scaled.MatchesApplied(window.Bounds)) return;
            CaptureBaseSize(scaled);
        };
    }

    private static bool IsApplicationMainWindow(Window window)
    {
        var lifetime = Application.Current?.ApplicationLifetime
            as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
        return lifetime != null && ReferenceEquals(lifetime.MainWindow, window);
    }

    private void ApplyTransform(ScaledWindow scaled)
    {
        scaled.Root.LayoutTransform = new ScaleTransform(_scale, _scale);
    }

    private void ApplyMinSizes(ScaledWindow scaled)
    {
        var window = scaled.Window;
        var factor = _autoSizeWindows ? _scale : 1.0;

        if (scaled.BaseMinWidth > 0 && window.MinWidth > 0)
            window.MinWidth = scaled.BaseMinWidth * factor;
        if (scaled.BaseMinHeight > 0 && window.MinHeight > 0)
            window.MinHeight = scaled.BaseMinHeight * factor;
    }

    private void CaptureBaseSize(ScaledWindow scaled)
    {
        var window = scaled.Window;
        if (window.SizeToContent != SizeToContent.Manual || window.WindowState != WindowState.Normal)
        {
            scaled.BaseWidth = 0;
            scaled.BaseHeight = 0;
            return;
        }

        var bounds = window.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        scaled.BaseWidth = bounds.Width / _scale;
        scaled.BaseHeight = bounds.Height / _scale;
    }

    private void ResizeToScale(ScaledWindow scaled)
    {
        var window = scaled.Window;
        if (!_autoSizeWindows) return;
        if (scaled.BaseWidth <= 0 || scaled.BaseHeight <= 0) return;
        if (window.WindowState != WindowState.Normal || window.SizeToContent != SizeToContent.Manual) return;

        var (maxWidth, maxHeight) = WorkingAreaSize(window);

        Resize(scaled,
            Math.Min(scaled.BaseWidth * _scale, maxWidth),
            Math.Min(scaled.BaseHeight * _scale, maxHeight));
    }

    private void GrowToContent(ScaledWindow scaled)
    {
        var window = scaled.Window;
        if (window.WindowState != WindowState.Normal || window.SizeToContent != SizeToContent.Manual) return;

        var (maxWidth, maxHeight) = WorkingAreaSize(window);

        scaled.Root.Measure(new Size(maxWidth, maxHeight));
        var desired = scaled.Root.DesiredSize;
        if (desired.Width <= 0 || desired.Height <= 0) return;

        var bounds = window.Bounds;
        Resize(scaled,
            GrownSide(bounds.Width, desired.Width, maxWidth),
            GrownSide(bounds.Height, desired.Height, maxHeight));
    }

    private double GrownSide(double current, double desired, double max)
    {
        if (desired <= current) return current;
        return Math.Min(Math.Min(desired, current * _scale), max);
    }

    private void Resize(ScaledWindow scaled, double width, double height)
    {
        _resizing = true;
        try
        {
            scaled.Window.Width = width;
            scaled.Window.Height = height;
            scaled.NoteApplied(width, height);
        }
        finally
        {
            _resizing = false;
        }
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

    private sealed class ScaledWindow
    {
        public ScaledWindow(Window window, LayoutTransformControl root, double baseMinWidth, double baseMinHeight)
        {
            Window = window;
            Root = root;
            BaseMinWidth = baseMinWidth;
            BaseMinHeight = baseMinHeight;
        }

        public Window Window { get; }
        public LayoutTransformControl Root { get; }
        public double BaseMinWidth { get; }
        public double BaseMinHeight { get; }

        public double BaseWidth { get; set; }
        public double BaseHeight { get; set; }

        private double _appliedWidth;
        private double _appliedHeight;

        public void NoteApplied(double width, double height)
        {
            _appliedWidth = width;
            _appliedHeight = height;
        }

        public bool MatchesApplied(Rect bounds) =>
            Math.Abs(bounds.Width - _appliedWidth) < 1.0 && Math.Abs(bounds.Height - _appliedHeight) < 1.0;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
