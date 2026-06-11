using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using LlamaServerLauncher.Models;

namespace LlamaServerLauncher.Services;

public static class DialogPositionHelper
{
    public static void ApplySavedGeometry(Window window, Dictionary<string, DialogGeometry> geometryDict, string dialogKey)
    {
        if (!geometryDict.TryGetValue(dialogKey, out var geo))
            return;

        if (geo.Width > 0)
            window.Width = Math.Max(geo.Width, window.MinWidth);
        if (geo.Height > 0)
            window.Height = Math.Max(geo.Height, window.MinHeight);

        if (geo.Left.HasValue && geo.Top.HasValue)
        {
            var left = geo.Left.Value;
            var top = geo.Top.Value;
            if (left >= 0 && left < 5000 && top >= 0 && top < 3000)
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Position = new PixelPoint((int)left, (int)top);
            }
        }
    }
}
