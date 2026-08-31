using System;
using System.Collections.Generic;
using System.Linq;
using LlamaServerLauncher.Models;

public static class ConfigurationDiffTests
{
    private static ServerConfiguration Current() => new()
    {
        ExecutablePath = @"C:\llama\llama-server.exe",
        ModelPath = @"D:\models\qwen.gguf",
        ContextSize = 8192,
        GpuLayers = 99,
        ApiKey = "secret",
        Temperature = 0.7,
        FlashAttention = true,
        Port = 8080
    };

    private static ConfigChange? Row(List<ConfigChange> changes, string property) =>
        changes.FirstOrDefault(c => c.PropertyName == property);

    public static void Run(Harness h)
    {
        h.Section("Import diff");

        var same = Current();
        h.Check("identical configurations differ in nothing",
            ConfigurationDiff.Build(Current(), same).Count == 0,
            ConfigurationDiff.Build(Current(), same).Count.ToString());

        var emptyVsNull = Current();
        emptyVsNull.ModelsDir = null!;
        h.Check("an empty string and no value are the same thing",
            Row(ConfigurationDiff.Build(Current(), emptyVsNull), "ModelsDir") == null, "ok");

        var incoming = new ServerConfiguration
        {
            ModelPath = @"D:\models\gemma.gguf",
            ContextSize = 4096,
            GpuLayers = 99,
            Port = 8080
        };

        var full = ConfigurationDiff.Build(Current(), incoming);
        h.Check("only the differing fields are listed",
            full.Count == 6, string.Join(",", full.Select(c => c.PropertyName)));
        h.Check("an unchanged field is not listed", Row(full, "GpuLayers") == null, "ok");
        h.Check("the row carries both values",
            Row(full, "ContextSize") is { } ctx
                && ConfigurationDiff.Describe(ctx.OldValue) == "8192"
                && ConfigurationDiff.Describe(ctx.NewValue) == "4096",
            "ok");
        h.Check("the row knows the canonical flag",
            Row(full, "ContextSize")?.Flag == "--ctx-size", Row(full, "ContextSize")?.Flag ?? "null");
        h.Check("a profile has every change checked", full.All(c => c.Apply), "ok");
        h.Check("dropping a value is marked as clearing",
            Row(full, "ApiKey")?.ClearsValue == true, "ok");
        h.Check("replacing a value is not clearing",
            Row(full, "ModelPath")?.ClearsValue == false, "ok");

        var mentioned = ConfigurationDiff.PropertiesMentionedIn(new[]
        {
            "-m", @"D:\models\gemma.gguf", "-c", "4096", "--verbose-thinking"
        });
        h.Check("a known flag names its field", mentioned.Contains("ModelPath"), "ok");
        h.Check("a short flag names the same field as the long one", mentioned.Contains("ContextSize"), "ok");
        h.Check("an unknown flag lands in the custom arguments",
            mentioned.Contains("CustomArguments"), "ok");
        h.Check("a value is not mistaken for a flag", !mentioned.Contains("Port"), "ok");

        var script = ConfigurationDiff.Build(Current(), incoming, mentioned);
        h.Check("what the script names is checked", Row(script, "ContextSize")?.Apply == true, "ok");
        h.Check("what the script never names is not checked",
            Row(script, "ApiKey")?.Apply == false, "ok");
        h.Check("a field the script would empty is still listed",
            Row(script, "ExecutablePath") != null, "ok");

        var merged = ConfigurationDiff.Merge(Current(), incoming, new[] { "ContextSize" });
        h.Check("the picked value is taken over", merged.ContextSize == 4096, merged.ContextSize?.ToString() ?? "null");
        h.Check("everything else stays as it was on the form",
            merged.ApiKey == "secret" && merged.ModelPath == @"D:\models\qwen.gguf" && merged.FlashAttention == true,
            merged.ApiKey);

        var withCustom = new ServerConfiguration
        {
            CustomArguments = "--no-warmup",
            CustomArgumentToggleStates = new Dictionary<string, bool> { ["--no-warmup"] = true }
        };
        var customMerged = ConfigurationDiff.Merge(Current(), withCustom, new[] { "CustomArguments" });
        h.Check("the toggle states follow the custom arguments",
            customMerged.CustomArguments == "--no-warmup" && customMerged.CustomArgumentToggleStates.Count == 1,
            customMerged.CustomArgumentToggleStates.Count.ToString());

        var currentMcp = Current();
        currentMcp.McpServers.Add(new McpServerEntry { Name = "fs", Command = "npx" });
        var incomingMcp = Current();
        incomingMcp.McpServers.Add(new McpServerEntry { Name = "fs", Command = "npx" });
        h.Check("identical MCP lists are not a change",
            Row(ConfigurationDiff.Build(currentMcp, incomingMcp), "McpServers") == null, "ok");

        incomingMcp.McpServers[0].Command = "uvx";
        h.Check("a changed MCP entry is a change",
            Row(ConfigurationDiff.Build(currentMcp, incomingMcp), "McpServers") != null, "ok");

        var mcpMerged = ConfigurationDiff.Merge(currentMcp, incomingMcp, new[] { "McpServers" });
        h.Check("the merged MCP list is a copy, not the imported list itself",
            !ReferenceEquals(mcpMerged.McpServers[0], incomingMcp.McpServers[0]), "ok");

        h.Check("every configuration field has a row of its own",
            ConfigurationDiff.UncoveredProperties().Count == 0,
            string.Join(",", ConfigurationDiff.UncoveredProperties()));

        var mmap = new ServerConfiguration { Mmap = false };
        h.Check("a field is named by the plain flag, not the negated one",
            Row(ConfigurationDiff.Build(new ServerConfiguration { Mmap = true }, mmap), "Mmap")?.Flag == "--mmap",
            Row(ConfigurationDiff.Build(new ServerConfiguration { Mmap = true }, mmap), "Mmap")?.Flag ?? "null");

        h.Check("a label without a flag gets one",
            ConfigurationDiff.ComposeLabel("Host:", "--host") == "Host (--host)",
            ConfigurationDiff.ComposeLabel("Host:", "--host"));
        h.Check("a label that already names a flag is left alone",
            ConfigurationDiff.ComposeLabel("Models dir (--models-dir):", "--models-dir") == "Models dir (--models-dir)",
            ConfigurationDiff.ComposeLabel("Models dir (--models-dir):", "--models-dir"));
        h.Check("a short flag in the label counts too",
            ConfigurationDiff.ComposeLabel("Model (-m):", "--model") == "Model (-m)",
            ConfigurationDiff.ComposeLabel("Model (-m):", "--model"));
        h.Check("a field with no flag keeps its bare name",
            ConfigurationDiff.ComposeLabel("MCP servers", "") == "MCP servers",
            ConfigurationDiff.ComposeLabel("MCP servers", ""));

        h.Check("switches read as on and off",
            ConfigurationDiff.Describe(true) == "on" && ConfigurationDiff.Describe(false) == "off", "ok");
        h.Check("an empty value has no text", ConfigurationDiff.Describe("") == null, "ok");
        h.Check("numbers are culture free", ConfigurationDiff.Describe(0.7) == "0.7", ConfigurationDiff.Describe(0.7) ?? "null");
    }
}
