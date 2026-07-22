using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace LlamaServerLauncher.Services;

public class ToastItem
{
    public string Message { get; }
    public bool IsError { get; }
    public bool IsNeutral { get; }
    public bool IsClickable => OnClick != null;
    public Guid Id { get; } = Guid.NewGuid();
    public Action? OnClick { get; }

    public ToastItem(string message, bool isError = false, Action? onClick = null, bool isNeutral = false)
    {
        Message = message;
        IsError = isError;
        OnClick = onClick;
        IsNeutral = isNeutral;
    }
}

public class ToastService
{
    private const int DefaultDurationMs = 5000;

    private const int MaxVisibleToasts = 5;

    public ObservableCollection<ToastItem> Toasts { get; } = new();

    public void Show(string message, int durationMs = DefaultDurationMs, Action? onClick = null)
        => Add(new ToastItem(message, onClick: onClick), durationMs);

    public void ShowError(string message, int durationMs = 5000, Action? onClick = null)
        => Add(new ToastItem(message, isError: true, onClick: onClick), durationMs);

    public void ShowNeutral(string message, int durationMs = 5000, Action? onClick = null)
        => Add(new ToastItem(message, onClick: onClick, isNeutral: true), durationMs);

    private void Add(ToastItem toast, int durationMs)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Toasts.Add(toast);
            while (Toasts.Count > MaxVisibleToasts)
                Toasts.RemoveAt(0);
        });

        if (durationMs > 0)
        {
            _ = Task.Delay(durationMs).ContinueWith(_ =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => Toasts.Remove(toast));
            });
        }
    }

    public void Dismiss(ToastItem toast)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => Toasts.Remove(toast));
    }

    public void ClearAll()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Toasts.Clear();
        });
    }
}
