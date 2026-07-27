using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform;
using LlamaServerLauncher.Models;
using LlamaServerLauncher.Resources;

namespace LlamaServerLauncher.Services;

public static class HelpService
{
    public const string TopicMain = "help-main";
    public const string TopicCustom = "help-custom";
    public const string TopicGeneration = "help-generation";
    public const string TopicOptions = "help-options";
    public const string TopicSpeculative = "help-speculative";
    public const string TopicDocker = "help-docker";
    public const string TopicSettings = "help-settings";
    public const string TopicBenchmarks = "help-benchmarks";
    public const string TopicScenarios = "help-scenarios";

    private const string ReadmeEn = "https://github.com/pytraveler/LlamaServerLauncherAvalonia/blob/main/README.md";
    private const string ReadmeRu = "https://github.com/pytraveler/LlamaServerLauncherAvalonia/blob/main/README_ru.md";

    public static string Load(string topic)
    {
        bool ru = IsRussian();
        string body = ReadAsset(topic, ru) ?? ReadAsset(topic, !ru) ?? $"`{topic}`";
        return body.TrimEnd() + "\n\n---\n\n["
            + LocalizedStrings.Instance.HelpMoreInReadme + "]("
            + (ru ? ReadmeRu : ReadmeEn) + ")\n";
    }

    public static Task ShowAsync(Window owner, string topic, string title,
        Dictionary<string, DialogGeometry>? geometryDict = null, Action? onClosed = null)
    {
        return MarkdownViewerWindow.ShowAsync(owner, Load(topic), title, geometryDict, onClosed);
    }

    private static bool IsRussian() =>
        LocalizedStrings.CurrentCulture.TwoLetterISOLanguageName == "ru";

    private static string? ReadAsset(string topic, bool ru)
    {
        var uri = new Uri($"avares://LlamaServerLauncher/Resources/Docs/{topic}.{(ru ? "ru" : "en")}.md");
        try
        {
            using var stream = AssetLoader.Open(uri);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch
        {
            return null;
        }
    }
}
