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
    public ObservableCollection<ToastItem> Toasts { get; } = new();

    public void Show(string message, int durationMs = 0, Action? onClick = null)
    {
        var toast = new ToastItem(message, onClick: onClick);
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Toasts.Add(toast);
        });

        if (durationMs > 0)
        {
            _ = Task.Delay(durationMs).ContinueWith(_ =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    Toasts.Remove(toast);
                });
            });
        }
    }

    public void ShowError(string message, int durationMs = 5000, Action? onClick = null)
    {
        var toast = new ToastItem(message, isError: true, onClick: onClick);
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Toasts.Add(toast);
        });

        if (durationMs > 0)
        {
            _ = Task.Delay(durationMs).ContinueWith(_ =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    Toasts.Remove(toast);
                });
            });
        }
    }

    public void ShowNeutral(string message, int durationMs = 5000, Action? onClick = null)
    {
        var toast = new ToastItem(message, onClick: onClick, isNeutral: true);
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Toasts.Add(toast);
        });

        if (durationMs > 0)
        {
            _ = Task.Delay(durationMs).ContinueWith(_ =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    Toasts.Remove(toast);
                });
            });
        }
    }

    public void Dismiss(ToastItem toast)
    {
        Toasts.Remove(toast);
    }

    public void ClearAll()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Toasts.Clear();
        });
    }
}
