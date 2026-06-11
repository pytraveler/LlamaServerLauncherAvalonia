using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using LlamaServerLauncher.Resources;
using LlamaServerLauncher.Services;

namespace LlamaServerLauncher.ViewModels;

public class ExperimentalRepoDialogViewModel : INotifyPropertyChanged
{
    public LocalizedStrings Localized => LocalizedStrings.Instance;

    private string _repoUrl = "";
    public string RepoUrl
    {
        get => _repoUrl;
        set { _repoUrl = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanSave)); UpdateAutoDisplayName(); Validate(); }
    }

    private string _displayName = "";
    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (_displayName == value) return;
            _displayName = value;
            _isAutoDisplayName = string.IsNullOrEmpty(value);
            OnPropertyChanged();
        }
    }

    private string _filterTags = "";
    public string FilterTags
    {
        get => _filterTags;
        set { _filterTags = value; OnPropertyChanged(); }
    }

    private string _validationError = "";
    public string ValidationError
    {
        get => _validationError;
        set { _validationError = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasValidationError)); }
    }

    public bool HasValidationError => !string.IsNullOrEmpty(ValidationError);

    public bool CanSave => !string.IsNullOrWhiteSpace(_repoUrl) && !HasValidationError;

    public bool IsEditMode { get; }

    public string DialogTitle => IsEditMode
        ? LocalizedStrings.GetString("ExperimentalRepoEditTitle")
        : LocalizedStrings.GetString("ExperimentalRepoDialogTitle");

    public bool Confirmed { get; private set; }

    public event Action? RequestClose;
    public event PropertyChangedEventHandler? PropertyChanged;

    public ExperimentalRepoDialogViewModel(bool isEditMode = false, string repoUrl = "", string displayName = "", string filterTags = "")
    {
        IsEditMode = isEditMode;
        _repoUrl = repoUrl;
        _displayName = displayName;
        _filterTags = filterTags;
        _isAutoDisplayName = string.IsNullOrEmpty(displayName);
        Validate();
    }

    private bool _isAutoDisplayName = true;

    private void UpdateAutoDisplayName()
    {
        if (!_isAutoDisplayName) return;
        _displayName = ExperimentalRepoService.TryParseGitHubUrl(_repoUrl, out _, out var repo) ? repo : "";
        OnPropertyChanged(nameof(DisplayName));
    }

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(_repoUrl))
        {
            ValidationError = "";
            return;
        }

        if (!ExperimentalRepoService.TryParseGitHubUrl(_repoUrl, out _, out _))
            ValidationError = LocalizedStrings.GetString("RepoUrlInvalid");
        else
            ValidationError = "";
    }

    public void Save()
    {
        if (!CanSave) return;
        if (string.IsNullOrWhiteSpace(DisplayName))
            UpdateAutoDisplayName();
        Confirmed = true;
        RequestClose?.Invoke();
    }

    public void Cancel()
    {
        RequestClose?.Invoke();
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
