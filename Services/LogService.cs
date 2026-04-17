using System;
using System.IO;
using System.Threading.Tasks;

namespace LlamaServerLauncher.Services;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

public class LogService
{
    private readonly string _logFilePath;
    private readonly object _lock = new();

    public event EventHandler<string>? LogReceived;

    public LogService()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LlamaServerLauncherAvalonia"
        );
        Directory.CreateDirectory(appDataPath);
        _logFilePath = Path.Combine(appDataPath, "app.log");
    }

    public void Log(LogLevel level, string message)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var logEntry = $"[{timestamp}] [{level}] {message}";

        lock (_lock)
        {
            try
            {
                File.AppendAllText(_logFilePath, logEntry + Environment.NewLine);
            }
            catch
            {
            }
        }

        try
        {
            LogReceived?.Invoke(this, logEntry);
        }
        catch (TaskCanceledException)
        {
            // Ignore - dispatcher is shutting down
        }
    }

    public void Debug(string message) => Log(LogLevel.Debug, message);
    public void Info(string message) => Log(LogLevel.Info, message);
    public void Warning(string message) => Log(LogLevel.Warning, message);
    public void Error(string message) => Log(LogLevel.Error, message);

    public void AppLog(string message)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var logEntry = $"[APP] {timestamp} {message}";
        
        lock (_lock)
        {
            try
            {
                File.AppendAllText(_logFilePath, logEntry + Environment.NewLine);
            }
            catch
            {
            }
        }

        try
        {
            LogReceived?.Invoke(this, logEntry);
        }
        catch (TaskCanceledException)
        {
            // Ignore - dispatcher is shutting down
        }
    }
}