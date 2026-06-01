using Avalonia;
using System;
using LlamaServerLauncher.Services;

namespace LlamaServerLauncher;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        SingleInstanceService? singleInstance = null;
        try
        {
            var resolver = new DataPathResolver();
            var appDataPath = resolver.ResolveDataPath();
            singleInstance = new SingleInstanceService(appDataPath);

            if (!singleInstance.TryAcquire())
            {
                singleInstance.SignalExistingInstance();
                return;
            }

            singleInstance.StartListening();
        }
        catch
        {
            singleInstance = null;
        }

        App.SingleInstance = singleInstance;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
