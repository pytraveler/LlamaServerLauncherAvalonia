using System;
using System.Collections.Generic;
using System.Linq;

namespace LlamaServerLauncher.Models;

public static class ProfileFavorites
{
    private static readonly object Gate = new();
    private static HashSet<string> _favorites = new(StringComparer.OrdinalIgnoreCase);
    private static string? _firstRegular;

    public static void Set(IEnumerable<string>? names)
    {
        lock (Gate)
        {
            _favorites = names == null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(names.Where(n => !string.IsNullOrWhiteSpace(n)), StringComparer.OrdinalIgnoreCase);
        }
    }

    public static List<string> Names
    {
        get
        {
            lock (Gate)
                return _favorites.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
    }

    public static bool IsFavorite(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        lock (Gate)
            return _favorites.Contains(name);
    }

    public static bool Toggle(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        lock (Gate)
        {
            if (_favorites.Remove(name))
                return false;

            _favorites.Add(name);
            return true;
        }
    }

    public static void Remove(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return;

        lock (Gate)
            _favorites.Remove(name);
    }

    public static void Rename(string? oldName, string? newName)
    {
        if (string.IsNullOrEmpty(oldName) || string.IsNullOrWhiteSpace(newName))
            return;

        lock (Gate)
        {
            if (_favorites.Remove(oldName))
                _favorites.Add(newName);
        }
    }

    public static List<string> Order(IEnumerable<string>? names)
    {
        var source = names?.ToList() ?? new List<string>();
        var favorites = new List<string>();
        var regular = new List<string>();

        foreach (var name in source)
        {
            if (IsFavorite(name))
                favorites.Add(name);
            else
                regular.Add(name);
        }

        lock (Gate)
            _firstRegular = favorites.Count > 0 && regular.Count > 0 ? regular[0] : null;

        favorites.AddRange(regular);
        return favorites;
    }

    public static bool IsFirstAfterFavorites(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        lock (Gate)
            return _firstRegular != null && string.Equals(_firstRegular, name, StringComparison.Ordinal);
    }

    public static void Clear()
    {
        lock (Gate)
        {
            _favorites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _firstRegular = null;
        }
    }
}
