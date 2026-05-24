using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace LlamaServerLauncher.Services;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

public class LogService : IDisposable
{
    private readonly string _logFilePath;
    private readonly string _baseLogPath;
    private readonly object _lock = new();
    private StreamWriter _writer;
    private bool _disposed;
    private int _maxLogFiles = 5;
    private long _maxLogSizeBytes = 10 * 1024 * 1024;

    public event EventHandler<string>? LogReceived;

    public string LogFilePath => _logFilePath;
    public int MaxLogFiles => _maxLogFiles;
    public long MaxLogSizeBytes => _maxLogSizeBytes;

    public LogService(string? appDataPath = null)
    {
        var basePath = appDataPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LlamaServerLauncherAvalonia"
        );
        Directory.CreateDirectory(basePath);
        _logFilePath = Path.Combine(basePath, "app.log");
        _baseLogPath = Path.Combine(basePath, "app");
        _writer = CreateWriter();
    }

    private StreamWriter CreateWriter()
    {
        var fileStream = new FileStream(_logFilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
        return new StreamWriter(fileStream, Encoding.UTF8);
    }

    public void Configure(int maxLogFiles, long maxLogSizeBytes)
    {
        lock (_lock)
        {
            if (maxLogFiles > 0) _maxLogFiles = maxLogFiles;
            if (maxLogSizeBytes > 0) _maxLogSizeBytes = maxLogSizeBytes;
        }
    }

    private void WriteLine(string line)
    {
        lock (_lock)
        {
            try
            {
                _writer.WriteLine(line);
                _writer.Flush();
                CheckRotation();
            }
            catch
            {
            }
        }
    }

    private void CheckRotation()
    {
        try
        {
            var fileLength = _writer.BaseStream.Length;
            if (fileLength < _maxLogSizeBytes) return;

            _writer.Dispose();

            for (var i = _maxLogFiles - 1; i >= 1; i--)
            {
                var source = $"{_baseLogPath}.{i}.log";
                var target = $"{_baseLogPath}.{i + 1}.log";
                if (File.Exists(source))
                {
                    if (File.Exists(target)) File.Delete(target);
                    File.Move(source, target);
                }
            }

            var firstBackup = $"{_baseLogPath}.1.log";
            if (File.Exists(firstBackup)) File.Delete(firstBackup);
            File.Move(_logFilePath, firstBackup);

            _writer = CreateWriter();
        }
        catch
        {
            try { _writer = CreateWriter(); } catch { }
        }
    }

    public void Log(LogLevel level, string message)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var logEntry = $"[{timestamp}] [{level}] {message}";

        WriteLine(logEntry);

        try
        {
            LogReceived?.Invoke(this, logEntry);
        }
        catch (TaskCanceledException)
        {
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

        WriteLine(logEntry);

        try
        {
            LogReceived?.Invoke(this, logEntry);
        }
        catch (TaskCanceledException)
        {
        }
    }

    public void LogRaw(string line)
    {
        WriteLine(line);
    }

    public void Flush()
    {
        lock (_lock)
        {
            try
            {
                _writer.Flush();
            }
            catch
            {
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_lock)
        {
            try
            {
                _writer.Flush();
                _writer.Dispose();
            }
            catch
            {
            }
        }
    }
}
