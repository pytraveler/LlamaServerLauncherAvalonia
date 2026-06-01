using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LlamaServerLauncher.Services;

public class SingleInstanceService : IDisposable
{
    private readonly string _lockFilePath;
    private readonly string _socketPath;
    private FileStream? _lockStream;
    private Mutex? _mutex;
    private bool _ownsMutex;
    private bool _isFirstInstance;
    private bool _disposed;
    private CancellationTokenSource? _cts;
    private bool _pendingActivation;

    public event Action? ActivateRequested;

    public SingleInstanceService(string appDataPath)
    {
        _lockFilePath = Path.Combine(appDataPath, "launcher.lock");
        _socketPath = Path.Combine(appDataPath, "launcher.sock");
    }

    public bool TryAcquire()
    {
        if (OperatingSystem.IsWindows())
        {
            _mutex = new Mutex(false, "LlamaServerLauncherAvalonia_SingleInstance");
            try
            {
                if (!_mutex.WaitOne(0))
                {
                    _mutex.Dispose();
                    _mutex = null;
                    return false;
                }
                _ownsMutex = true;
            }
            catch (AbandonedMutexException)
            {
                _ownsMutex = true;
            }
        }

        if (!TryAcquireLockFile())
        {
            if (!OperatingSystem.IsWindows())
            {
                ReleaseResources();
                return false;
            }
        }

        _isFirstInstance = true;
        return true;
    }

    private bool TryAcquireLockFile()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_lockFilePath)!);
            _lockStream = new FileStream(_lockFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
            WriteLockFile();
            return true;
        }
        catch (IOException)
        {
            return TryAcquireStaleLock();
        }
    }

    private void WriteLockFile()
    {
        var startTime = Process.GetCurrentProcess().StartTime.ToString("o");
        var content = $"{Environment.ProcessId}\n{startTime}";
        var bytes = Encoding.UTF8.GetBytes(content);
        _lockStream!.Write(bytes, 0, bytes.Length);
        _lockStream.Flush();
    }

    private bool TryAcquireStaleLock()
    {
        bool isStale = false;
        try
        {
            var content = File.ReadAllText(_lockFilePath).Trim();
            var lines = content.Split('\n');
            if (int.TryParse(lines[0], out int pid) && pid > 0)
            {
                try
                {
                    using var proc = Process.GetProcessById(pid);
                    if (lines.Length > 1)
                    {
                        if (DateTime.TryParse(lines[1], out var startTime))
                        {
                            isStale = proc.StartTime != startTime;
                        }
                        else
                        {
                            isStale = false;
                        }
                    }
                    else
                    {
                        isStale = false;
                    }
                }
                catch (ArgumentException)
                {
                    isStale = true;
                }
                catch (InvalidOperationException)
                {
                    isStale = true;
                }
            }
            else
            {
                isStale = true;
            }
        }
        catch
        {
            isStale = true;
        }

        if (!isStale) return false;

        try
        {
            try { _lockStream?.Dispose(); _lockStream = null; } catch { }
            File.Delete(_lockFilePath);
            _lockStream = new FileStream(_lockFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
            WriteLockFile();
            return true;
        }
        catch { }

        return false;
    }

    public void StartListening()
    {
        if (!_isFirstInstance) return;
        _cts = new CancellationTokenSource();

        if (OperatingSystem.IsWindows())
            _ = ListenNamedPipeAsync(_cts.Token);
        else
            _ = ListenUnixSocketAsync(_cts.Token);
    }

    private async Task ListenNamedPipeAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    "LlamaServerLauncherAvalonia_IPC",
                    PipeDirection.In, 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(ct);
                using var reader = new StreamReader(server, Encoding.UTF8);
                var message = await reader.ReadLineAsync();
                if (message == "activate")
                    OnActivateRequested();
            }
            catch (OperationCanceledException) { break; }
            catch
            {
                try { await Task.Delay(100, ct); } catch { break; }
            }
        }
    }

    private async Task ListenUnixSocketAsync(CancellationToken ct)
    {
        Socket? listener = null;
        try
        {
            CleanUpSocketFile();
            var endpoint = new UnixDomainSocketEndPoint(_socketPath);
            listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(endpoint);
            listener.Listen(1);

            using (ct.Register(() =>
            {
                try { listener.Dispose(); } catch { }
            }))
            {
                while (!ct.IsCancellationRequested)
                {
                    Socket? client = null;
                    try
                    {
                        client = await listener.AcceptAsync();
                        var buffer = new byte[256];
                        var bytesRead = await client.ReceiveAsync(new ArraySegment<byte>(buffer), SocketFlags.None);
                        var message = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                        if (message == "activate")
                            OnActivateRequested();
                    }
                    catch (SocketException) { break; }
                    catch (ObjectDisposedException) { break; }
                    finally { client?.Dispose(); }
                }
            }
        }
        catch (ObjectDisposedException) { }
        catch (SocketException) { }
        finally
        {
            listener?.Dispose();
            CleanUpSocketFile();
        }
    }

    private void OnActivateRequested()
    {
        _pendingActivation = true;
        ActivateRequested?.Invoke();
    }

    public bool ConsumePendingActivation()
    {
        if (!_pendingActivation) return false;
        _pendingActivation = false;
        return true;
    }

    public void SignalExistingInstance()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var client = new NamedPipeClientStream(".", "LlamaServerLauncherAvalonia_IPC", PipeDirection.Out);
                client.Connect(3000);
                using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
                writer.WriteLine("activate");
            }
            else
            {
                var endpoint = new UnixDomainSocketEndPoint(_socketPath);
                using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                socket.Connect(endpoint);
                var message = Encoding.UTF8.GetBytes("activate\n");
                socket.Send(message);
            }
        }
        catch { }
    }

    private void CleanUpSocketFile()
    {
        try { if (File.Exists(_socketPath)) File.Delete(_socketPath); } catch { }
    }

    private void ReleaseResources()
    {
        try { _lockStream?.Dispose(); _lockStream = null; } catch { }
        try { if (File.Exists(_lockFilePath)) File.Delete(_lockFilePath); } catch { }
        CleanUpSocketFile();
        try
        {
            if (_mutex != null && _ownsMutex)
            {
                _mutex.ReleaseMutex();
            }
            _mutex?.Dispose();
            _mutex = null;
        }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
        ReleaseResources();
    }
}
