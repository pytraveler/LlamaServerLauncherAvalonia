using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using LlamaServerLauncher.Models;
using LlamaServerLauncher.Resources;

namespace LlamaServerLauncher.ViewModels;

public class ArgumentPickerViewModel : INotifyPropertyChanged
{
    public LocalizedStrings Localized { get; } = LocalizedStrings.Instance;

    private readonly List<HelpArgumentItem> _allArguments;
    public ObservableCollection<HelpArgumentItem> FilteredArguments { get; } = new();

    private string _filterText = "";
    public string FilterText
    {
        get => _filterText;
        set
        {
            if (_filterText != value)
            {
                _filterText = value;
                OnPropertyChanged();
                ApplyFilter();
            }
        }
    }

    public ICommand AddSelectedCommand { get; }
    public ICommand CancelCommand { get; }

    public event Action? RequestClose;

    public ArgumentPickerViewModel(List<HelpArgumentInfo> arguments)
    {
        _allArguments = arguments.Select(a => new HelpArgumentItem
        {
            PrimaryFlag = a.PrimaryFlag,
            Description = a.Description,
            DefaultValue = a.DefaultValue,
            IsSelected = false
        }).ToList();

        ApplyFilter();

        AddSelectedCommand = new RelayCommand(_ => RequestClose?.Invoke());
        CancelCommand = new RelayCommand(_ => RequestClose?.Invoke());
    }

    private void ApplyFilter()
    {
        FilteredArguments.Clear();
        var filter = _filterText.Trim();
        foreach (var item in _allArguments)
        {
            if (string.IsNullOrEmpty(filter) ||
                item.PrimaryFlag.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                item.Description.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                FilteredArguments.Add(item);
            }
        }
    }

    public List<HelpArgumentItem> GetSelectedItems() =>
        _allArguments.Where(x => x.IsSelected).ToList();

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class HelpArgumentItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public string PrimaryFlag { get; set; } = "";
    public string Description { get; set; } = "";
    public string? DefaultValue { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
