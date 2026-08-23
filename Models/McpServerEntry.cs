using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace LlamaServerLauncher.Models;

public class McpServerEntry : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _command = string.Empty;
    private string _argsText = string.Empty;
    private string _envText = string.Empty;
    private string _workingDirectory = string.Empty;
    private int? _timeoutMs;
    private bool _enabled = true;

    [JsonPropertyName("name")]
    public string Name
    {
        get => _name;
        set => Set(ref _name, value ?? string.Empty);
    }

    [JsonPropertyName("command")]
    public string Command
    {
        get => _command;
        set => Set(ref _command, value ?? string.Empty);
    }

    [JsonPropertyName("args")]
    public string ArgsText
    {
        get => _argsText;
        set => Set(ref _argsText, value ?? string.Empty);
    }

    [JsonPropertyName("env")]
    public string EnvText
    {
        get => _envText;
        set => Set(ref _envText, value ?? string.Empty);
    }

    [JsonPropertyName("cwd")]
    public string WorkingDirectory
    {
        get => _workingDirectory;
        set => Set(ref _workingDirectory, value ?? string.Empty);
    }

    [JsonPropertyName("timeoutMs")]
    public int? TimeoutMs
    {
        get => _timeoutMs;
        set => Set(ref _timeoutMs, value);
    }

    [JsonPropertyName("enabled")]
    public bool Enabled
    {
        get => _enabled;
        set => Set(ref _enabled, value);
    }

    public McpServerEntry Clone() => new()
    {
        Name = Name,
        Command = Command,
        ArgsText = ArgsText,
        EnvText = EnvText,
        WorkingDirectory = WorkingDirectory,
        TimeoutMs = TimeoutMs,
        Enabled = Enabled
    };

    public bool SameAs(McpServerEntry? other)
    {
        if (other == null) return false;
        return Name == other.Name
            && Command == other.Command
            && ArgsText == other.ArgsText
            && EnvText == other.EnvText
            && WorkingDirectory == other.WorkingDirectory
            && TimeoutMs == other.TimeoutMs
            && Enabled == other.Enabled;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
