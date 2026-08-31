using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using LlamaServerLauncher.Models;
using LlamaServerLauncher.Resources;

namespace LlamaServerLauncher.ViewModels;

public sealed class ImportChangeItem : INotifyPropertyChanged
{
    private readonly ConfigChange _change;
    private readonly Action _selectionChanged;
    private bool _isSelected;

    public ImportChangeItem(ConfigChange change, Action selectionChanged)
    {
        _change = change;
        _selectionChanged = selectionChanged;
        _isSelected = change.Apply;
    }

    public string PropertyName => _change.PropertyName;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
            _selectionChanged();
        }
    }

    public string Label => ConfigurationDiff.ComposeLabel(
        LocalizedStrings.GetString(_change.LabelKey), _change.Flag);

    public string OldText => Describe(_change.OldValue);
    public string NewText => Describe(_change.NewValue);

    public bool ClearsValue => _change.ClearsValue;

    public void SetSelectedSilently(bool value)
    {
        if (_isSelected == value) return;
        _isSelected = value;
        OnPropertyChanged(nameof(IsSelected));
    }

    private static string Describe(object? value)
    {
        if (value is bool flag)
            return LocalizedStrings.GetString(flag ? "ImportValueOn" : "ImportValueOff");

        return ConfigurationDiff.Describe(value) ?? LocalizedStrings.GetString("ImportValueNotSet");
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class ImportChangeGroup
{
    public ImportChangeGroup(string headerKey, IEnumerable<ImportChangeItem> items)
    {
        HeaderKey = headerKey;
        foreach (var item in items)
            Items.Add(item);
    }

    public string HeaderKey { get; }

    public string Header => LocalizedStrings.GetString(HeaderKey);

    public ObservableCollection<ImportChangeItem> Items { get; } = new();
}
