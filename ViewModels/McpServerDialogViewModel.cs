using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using LlamaServerLauncher.Models;
using LlamaServerLauncher.Resources;
using LlamaServerLauncher.Services;

namespace LlamaServerLauncher.ViewModels;

public class McpServerDialogViewModel : INotifyPropertyChanged
{
    public LocalizedStrings Localized { get; } = LocalizedStrings.Instance;

    private string _validationText = string.Empty;
    private string _probeText = string.Empty;
    private bool _isProbing;

    public McpServerDialogViewModel() : this(new McpServerEntry())
    {
    }

    public McpServerDialogViewModel(McpServerEntry entry)
    {
        Entry = entry;
        Entry.PropertyChanged += (_, _) => Revalidate();
        Revalidate();
    }

    public McpServerEntry Entry { get; }

    public int TimeoutMs
    {
        get => Entry.TimeoutMs ?? McpConfigDocument.DefaultTimeoutMs;
        set
        {
            var clamped = value < 0 ? 0 : value;
            if (Entry.TimeoutMs == clamped) return;
            Entry.TimeoutMs = clamped;
            OnPropertyChanged();
        }
    }

    public string ValidationText
    {
        get => _validationText;
        private set
        {
            if (_validationText == value) return;
            _validationText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasValidationIssues));
        }
    }

    public bool HasValidationIssues => _validationText.Length > 0;

    public string ProbeText
    {
        get => _probeText;
        private set
        {
            if (_probeText == value) return;
            _probeText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasProbeText));
        }
    }

    public bool HasProbeText => _probeText.Length > 0;

    public bool IsProbing
    {
        get => _isProbing;
        private set
        {
            if (_isProbing == value) return;
            _isProbing = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanProbe));
        }
    }

    public bool CanProbe => !_isProbing;

    public event Action? RequestClose;

    public async Task BrowseCommandAsync()
    {
        var result = await WindowsFileDialogs.OpenFileDialogAsync(
            Localized.McpCommand,
            new[] { ("All files", new[] { "*" }) },
            false);

        if (result != null && result.Length > 0)
            Entry.Command = result[0];
    }

    public async Task BrowseWorkingDirectoryAsync()
    {
        var result = await WindowsFileDialogs.OpenFolderDialogAsync(Localized.McpCwd);
        if (!string.IsNullOrEmpty(result))
            Entry.WorkingDirectory = result;
    }

    public async Task ProbeAsync()
    {
        if (IsProbing) return;

        var name = string.IsNullOrWhiteSpace(Entry.Name) ? Entry.Command : Entry.Name;
        IsProbing = true;
        ProbeText = string.Format(Localized.McpTestRunning, name);

        try
        {
            var timeout = Entry.TimeoutMs is int value && value > 0 ? value : McpConfigDocument.DefaultTimeoutMs;
            var probe = await McpProbeService.ProbeAsync(Entry, Math.Min(timeout, 30000));

            if (probe.Success)
            {
                var tools = probe.Tools.Count > 0 ? string.Join(", ", probe.Tools) : "-";
                ProbeText = string.Format(Localized.McpTestOk, name, probe.Tools.Count, tools);
            }
            else
            {
                ProbeText = string.Format(Localized.McpTestFailed, name, probe.Error);
            }
        }
        finally
        {
            IsProbing = false;
        }
    }

    public void Close() => RequestClose?.Invoke();

    private void Revalidate()
    {
        ValidationText = McpValidationFormatter.Format(
            McpConfigDocument.Validate(new List<McpServerEntry> { Entry }));
        OnPropertyChanged(nameof(TimeoutMs));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
