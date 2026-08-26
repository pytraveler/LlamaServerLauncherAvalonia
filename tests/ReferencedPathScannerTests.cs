using System;
using System.Collections.Generic;
using System.Linq;
using LlamaServerLauncher.Models;

public static class ReferencedPathScannerTests
{
    private const string Exe = @"C:\llama\llama-server.exe";
    private const string Model = @"D:\models\qwen.gguf";
    private const string Mmproj = @"D:\models\mmproj.gguf";
    private const string Draft = @"D:\models\draft.gguf";
    private const string ModelsDir = @"D:\models";
    private const string Gone = @"E:\gone\model.gguf";

    private static ReferencedPathProbe ProbeWith(params string[] existing)
    {
        var present = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        return new ReferencedPathProbe
        {
            FileExists = p => present.Contains(p),
            DirectoryExists = p => present.Contains(p)
        };
    }

    private static ServerConfiguration FullConfig() => new()
    {
        ExecutablePath = Exe,
        ModelPath = Model,
        MmprojPath = Mmproj,
        SpecDraftModel = Draft
    };

    private static string Labels(List<ReferencedPath> missing) =>
        string.Join(",", missing.Select(m => string.IsNullOrEmpty(m.LabelKey) ? m.Label : m.LabelKey));

    private static Dictionary<string, IReadOnlyList<ReferencedPath>> Broken(string labelKey, string path) => new()
    {
        ["Qwen"] = new List<ReferencedPath> { new() { LabelKey = labelKey, Path = path } }
    };

    public static void Run(Harness h)
    {
        h.Section("ReferencedPathScanner - profile fields");

        var all = ReferencedPathScanner.FindMissing(FullConfig(), ProbeWith(Exe, Model, Mmproj, Draft));
        h.Check("everything in place -> nothing missing", all.Count == 0, Labels(all));

        var noModel = ReferencedPathScanner.FindMissing(FullConfig(), ProbeWith(Exe, Mmproj, Draft));
        h.Check("moved model reported once", noModel.Count == 1 && noModel[0].LabelKey == "ModelM", Labels(noModel));
        h.Check("reported path is the configured one", noModel[0].Path == Model, noModel[0].Path);

        var noProjector = ReferencedPathScanner.FindMissing(FullConfig(), ProbeWith(Exe, Model, Draft));
        h.Check("moved mmproj reported", Labels(noProjector) == "MMProj", Labels(noProjector));

        var noDraft = ReferencedPathScanner.FindMissing(FullConfig(), ProbeWith(Exe, Model, Mmproj));
        h.Check("moved draft model reported", Labels(noDraft) == "SpecDraftModel", Labels(noDraft));

        var nothingThere = ReferencedPathScanner.FindMissing(FullConfig(), ProbeWith());
        h.Check("whole folder moved -> every path reported", nothingThere.Count == 4, Labels(nothingThere));

        var routing = new ServerConfiguration { ExecutablePath = Exe, ModelsDir = ModelsDir };
        h.Check("routing profile checks the directory",
            Labels(ReferencedPathScanner.FindMissing(routing, ProbeWith(Exe))) == "ModelsDir",
            Labels(ReferencedPathScanner.FindMissing(routing, ProbeWith(Exe))));
        h.Check("existing directory is fine",
            ReferencedPathScanner.FindMissing(routing, ProbeWith(Exe, ModelsDir)).Count == 0, "ok");

        var fromHub = new ServerConfiguration { ExecutablePath = Exe, HfRepo = "user/repo", HfFile = "q4.gguf" };
        h.Check("hugging face profile has nothing local to check",
            ReferencedPathScanner.FindMissing(fromHub, ProbeWith(Exe)).Count == 0, "ok");

        h.Section("ReferencedPathScanner - executable");

        var byName = new ServerConfiguration { ExecutablePath = "llama-server", ModelPath = Model };

        var resolved = ReferencedPathScanner.FindMissing(byName, new ReferencedPathProbe
        {
            FileExists = p => p == Model,
            DirectoryExists = _ => false,
            ExecutableResolver = _ => Exe
        });
        h.Check("executable found through PATH is not missing", resolved.Count == 0, Labels(resolved));

        var unresolved = ReferencedPathScanner.FindMissing(byName, new ReferencedPathProbe
        {
            FileExists = p => p == Model,
            DirectoryExists = _ => false,
            ExecutableResolver = _ => null
        });
        h.Check("executable resolvable nowhere is missing", Labels(unresolved) == "LlamaServerExe", Labels(unresolved));

        h.Section("ReferencedPathScanner - docker");

        var docker = FullConfig();
        docker.RunInDocker = true;
        h.Check("container paths are not checked against the local disk",
            ReferencedPathScanner.FindMissing(docker, ProbeWith()).Count == 0, "ok");

        h.Section("ReferencedPathScanner - custom arguments");

        var overridden = FullConfig();
        overridden.CustomArguments = "-m \"" + Draft + "\"";
        var overriddenResult = ReferencedPathScanner.FindMissing(overridden, ProbeWith(Exe, Mmproj, Draft));
        h.Check("custom -m wins over the stale field", overriddenResult.Count == 0, Labels(overriddenResult));

        var overriddenMissing = FullConfig();
        overriddenMissing.CustomArguments = "--model \"" + Gone + "\"";
        var overrideResult = ReferencedPathScanner.FindMissing(overriddenMissing, ProbeWith(Exe, Model, Mmproj, Draft));
        h.Check("missing custom -m is reported instead of the field",
            Labels(overrideResult) == "ModelM" && overrideResult[0].Path == Gone,
            Labels(overrideResult) + " " + (overrideResult.Count > 0 ? overrideResult[0].Path : ""));

        var toggledOff = FullConfig();
        toggledOff.CustomArguments = "-m \"" + Gone + "\"";
        toggledOff.CustomArgumentToggleStates = new Dictionary<string, bool> { ["-m"] = false };
        h.Check("disabled custom argument falls back to the field",
            ReferencedPathScanner.FindMissing(toggledOff, ProbeWith(Exe, Model, Mmproj, Draft)).Count == 0, "ok");

        var template = FullConfig();
        template.CustomArguments = "--chat-template-file \"E:\\gone\\template.jinja\"";
        var templateResult = ReferencedPathScanner.FindMissing(template, ProbeWith(Exe, Model, Mmproj, Draft));
        h.Check("missing file behind an extra flag is reported",
            Labels(templateResult) == "--chat-template-file", Labels(templateResult));

        var relative = FullConfig();
        relative.CustomArguments = "--chat-template-file template.jinja";
        h.Check("relative path is left alone",
            ReferencedPathScanner.FindMissing(relative, ProbeWith(Exe, Model, Mmproj, Draft)).Count == 0, "ok");

        var unknownFlag = FullConfig();
        unknownFlag.CustomArguments = "--some-flag \"E:\\gone\\whatever.bin\"";
        h.Check("value of an unrelated flag is not treated as a path",
            ReferencedPathScanner.FindMissing(unknownFlag, ProbeWith(Exe, Model, Mmproj, Draft)).Count == 0, "ok");

        h.Section("ProfilePathStatus");

        ProfilePathStatus.Clear();
        h.Check("clean state has no broken profiles", !ProfilePathStatus.IsBroken("Any"), "ok");

        h.Check("first update changes the state", ProfilePathStatus.Update(Broken("ModelM", Gone)), "ok");
        h.Check("broken profile is known", ProfilePathStatus.IsBroken("Qwen"), "ok");
        h.Check("lookup ignores case", ProfilePathStatus.IsBroken("qwen"), "ok");
        h.Check("entries are kept as scanned",
            ProfilePathStatus.MissingFor("Qwen") is { Count: 1 } kept
                && kept[0].LabelKey == "ModelM" && kept[0].Path == Gone,
            ProfilePathStatus.MissingFor("Qwen")?[0].Path ?? "null");
        h.Check("healthy profile stays clean", !ProfilePathStatus.IsBroken("Gemma"), "ok");
        h.Check("nothing to show for a healthy profile", ProfilePathStatus.MissingFor("Gemma") == null, "ok");

        h.Check("same state again is not a change", !ProfilePathStatus.Update(Broken("ModelM", Gone)), "ok");
        h.Check("another field counts as a change", ProfilePathStatus.Update(Broken("MMProj", Gone)), "ok");
        h.Check("another path counts as a change", ProfilePathStatus.Update(Broken("MMProj", Draft)), "ok");
        h.Check("emptying the lookup is a change",
            ProfilePathStatus.Update(new Dictionary<string, IReadOnlyList<ReferencedPath>>()), "ok");
        h.Check("nothing is broken afterwards", !ProfilePathStatus.IsBroken("Qwen"), "ok");
        h.Check("empty over empty is not a change",
            !ProfilePathStatus.Update(new Dictionary<string, IReadOnlyList<ReferencedPath>>()), "ok");

        ProfilePathStatus.Clear();
    }
}
