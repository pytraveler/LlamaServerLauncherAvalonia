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
    public ObservableCollection<ArgumentCategoryGroup> GroupedArguments { get; } = new();

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
            AllowedValues = a.AllowedValues,
            Category = a.Category,
            IsSelected = false
        }).ToList();

        ApplyFilter();

        AddSelectedCommand = new RelayCommand(_ => RequestClose?.Invoke());
        CancelCommand = new RelayCommand(_ => RequestClose?.Invoke());
    }

    private void ApplyFilter()
    {
        GroupedArguments.Clear();
        var filter = _filterText.Trim();

        var filtered = string.IsNullOrEmpty(filter)
            ? _allArguments
            : _allArguments.Where(item =>
                item.PrimaryFlag.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                item.Description.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                (item.AllowedValues != null && item.AllowedValues.Any(v => v.Contains(filter, StringComparison.OrdinalIgnoreCase))));

        var categoryOrder = new List<string> { "common", "sampling", "speculative", "server" };

        var grouped = filtered
            .GroupBy(item => item.Category)
            .OrderBy(g => categoryOrder.IndexOf(g.Key) >= 0 ? categoryOrder.IndexOf(g.Key) : 999)
            .ThenBy(g => g.Key);

        foreach (var group in grouped)
        {
            var categoryGroup = new ArgumentCategoryGroup(
                LlamaArgumentRegistry.GetCategoryDisplayName(group.Key),
                group.OrderBy(item => item.PrimaryFlag, StringComparer.OrdinalIgnoreCase).ToList());
            GroupedArguments.Add(categoryGroup);
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

public class ArgumentCategoryGroup
{
    public string CategoryName { get; }
    public List<HelpArgumentItem> Items { get; }

    public ArgumentCategoryGroup(string categoryName, List<HelpArgumentItem> items)
    {
        CategoryName = categoryName;
        Items = items;
    }
}

public class HelpArgumentItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public string PrimaryFlag { get; set; } = "";
    public string Description { get; set; } = "";
    public string? DefaultValue { get; set; }
    public string Category { get; set; } = "common";
    public List<string>? AllowedValues { get; set; }

    public string AllowedValuesText => AllowedValues != null && AllowedValues.Count > 0
        ? string.Join(", ", AllowedValues)
        : "";

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
