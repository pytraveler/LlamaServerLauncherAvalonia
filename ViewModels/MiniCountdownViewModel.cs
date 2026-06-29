using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using LlamaServerLauncher.Resources;
using LlamaServerLauncher.Services;

namespace LlamaServerLauncher.ViewModels;

public class MiniCountdownViewModel : INotifyPropertyChanged
{
    public LocalizedStrings Localized { get; } = LocalizedStrings.Instance;

    private readonly Func<Task> _onHold;
    private readonly Func<Task> _onUnload;

    public MiniCountdownViewModel(Func<Task> onHold, Func<Task> onUnload)
    {
        _onHold = onHold;
        _onUnload = onUnload;
        PrimaryCommand = new AsyncRelayCommand(_ => IsHeld ? _onUnload() : _onHold());
    }

    public ICommand PrimaryCommand { get; }

    private string _profileName = "";
    public string ProfileName
    {
        get => _profileName;
        set { if (_profileName != value) { _profileName = value; OnPropertyChanged(); } }
    }

    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        set { if (_statusText != value) { _statusText = value; OnPropertyChanged(); } }
    }

    private string _countdownText = "";
    public string CountdownText
    {
        get => _countdownText;
        set { if (_countdownText != value) { _countdownText = value; OnPropertyChanged(); } }
    }

    private bool _showCountdown;
    public bool ShowCountdown
    {
        get => _showCountdown;
        set { if (_showCountdown != value) { _showCountdown = value; OnPropertyChanged(); } }
    }

    private bool _isHeld;
    public bool IsHeld
    {
        get => _isHeld;
        set
        {
            if (_isHeld != value)
            {
                _isHeld = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PrimaryButtonText));
            }
        }
    }

    public string PrimaryButtonText => IsHeld
        ? LocalizedStrings.GetString("MiniWindowUnloadModel")
        : LocalizedStrings.GetString("MiniWindowCancelUnload");

    public void SetLoaded(string profileName)
    {
        ProfileName = profileName;
        StatusText = LocalizedStrings.GetString("MiniWindowInUse");
        ShowCountdown = false;
        IsHeld = false;
    }

    public void Apply(ProxyActivity activity)
    {
        if (!string.IsNullOrEmpty(activity.ProfileName))
            ProfileName = activity.ProfileName!;

        switch (activity.State)
        {
            case ProxyActivityState.Serving:
                StatusText = LocalizedStrings.GetString("MiniWindowInUse");
                ShowCountdown = false;
                IsHeld = false;
                break;
            case ProxyActivityState.Held:
                StatusText = LocalizedStrings.GetString("MiniWindowKept");
                ShowCountdown = false;
                IsHeld = true;
                break;
            case ProxyActivityState.IdleCountdown:
                StatusText = string.Format(LocalizedStrings.GetString("MiniWindowUnloadingIn"), activity.RemainingSeconds);
                CountdownText = activity.RemainingSeconds.ToString();
                ShowCountdown = true;
                IsHeld = false;
                break;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
