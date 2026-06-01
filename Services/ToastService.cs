using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace LlamaServerLauncher.Services;

public class ToastItem
{
    public string Message { get; }
    public bool IsError { get; }
    public Guid Id { get; } = Guid.NewGuid();

    public ToastItem(string message, bool isError = false)
    {
        Message = message;
        IsError = isError;
    }
}

public class ToastService
{
    public ObservableCollection<ToastItem> Toasts { get; } = new();

    public void Show(string message, int durationMs = 0)
    {
        var toast = new ToastItem(message);
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

    public void ShowError(string message, int durationMs = 5000)
    {
        var toast = new ToastItem(message, isError: true);
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
