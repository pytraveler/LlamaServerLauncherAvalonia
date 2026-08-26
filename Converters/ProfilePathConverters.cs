using System;
using System.Globalization;
using Avalonia.Data.Converters;
using LlamaServerLauncher.Models;

namespace LlamaServerLauncher.Converters;

public class ProfilePathBrokenConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return ProfilePathStatus.IsBroken(value as string);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class ProfilePathTooltipConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var missing = ProfilePathStatus.MissingFor(value as string);
        if (missing == null || missing.Count == 0)
            return null;

        return Resources.ReferencedPathText.Describe(missing);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
