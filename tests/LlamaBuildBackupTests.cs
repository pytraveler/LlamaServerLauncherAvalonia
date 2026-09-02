using System;
using System.IO;
using LlamaServerLauncher.Services;

public static class LlamaBuildBackupTests
{
    private static string NewDataDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "llama-backup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteBuild(string directory, string tag)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, LlamaCppDownloadService.ServerExecutableName), tag);
        File.WriteAllText(Path.Combine(directory, "ggml.dll"), tag);
        LlamaCppDownloadService.RecordBuildTag(directory, tag);
    }

    private static string ReadBuild(string directory)
    {
        var path = Path.Combine(directory, LlamaCppDownloadService.ServerExecutableName);
        return File.Exists(path) ? File.ReadAllText(path) : "";
    }

    private static void RunVersionParsing(Harness h)
    {
        h.Section("llama.cpp version output");

        var current = "version: 0.3.0-dev (build 10726, commit 85c55223c)\nbuilt with Clang 20.1.8 for Windows x86_64";
        h.Check("the build number is taken from the current output",
            LlamaCppDownloadService.ParseVersionTag(current) == "b10726",
            LlamaCppDownloadService.ParseVersionTag(current) ?? "null");

        var legacy = "version: 6390 (85c55223c)\nbuilt with MSVC 19.29 for x64";
        h.Check("the older output is still understood",
            LlamaCppDownloadService.ParseVersionTag(legacy) == "b6390",
            LlamaCppDownloadService.ParseVersionTag(legacy) ?? "null");

        h.Check("the compiler line alone tells nothing",
            LlamaCppDownloadService.ParseVersionTag("built with Clang 20.1.8 for Windows x86_64") == null, "null");

        h.Check("a build without an embedded number stays unknown",
            LlamaCppDownloadService.ParseVersionTag("version: 0 (unknown)") == null, "null");

        h.Check("nothing at all is not a version", LlamaCppDownloadService.ParseVersionTag("") == null, "null");
    }

    public static void Run(Harness h)
    {
        RunVersionParsing(h);

        h.Section("llama.cpp build backup");

        var dataDir = NewDataDir();
        try
        {
            var service = new LlamaCppDownloadService(dataDir);
            var install = service.InstallDirectory;
            var backup = service.BackupDirectory;

            h.Check("nothing is kept before the first update", service.GetBackupInfo() == null, "no backup");

            WriteBuild(install, "b1000");
            var staging = service.StageForReplace(install, "b1000", keepAsBackup: true);
            h.Check("the old build is moved into the backup directory",
                staging.StagedPath == backup && ReadBuild(backup) == "b1000", staging.StagedPath ?? "null");
            h.Check("the install directory is left free for the new build",
                !Directory.Exists(install) || Directory.GetFileSystemEntries(install).Length == 0, "empty");

            Directory.CreateDirectory(install);
            File.WriteAllText(Path.Combine(install, "half-extracted.tmp"), "junk");
            staging.Rollback();
            h.Check("a failed extraction restores the old build", ReadBuild(install) == "b1000", ReadBuild(install));
            h.Check("the half-extracted leftovers are gone",
                !File.Exists(Path.Combine(install, "half-extracted.tmp")), "gone");

            staging = service.StageForReplace(install, "b1000", keepAsBackup: true);
            WriteBuild(install, "b2000");
            staging.Commit();

            var info = service.GetBackupInfo();
            h.Check("after an update the replaced build is kept", info != null, info == null ? "null" : info.Directory);
            h.Check("the kept build remembers its version", info?.Tag == "b1000", info?.Tag ?? "null");
            h.Check("the kept build reports its size", (info?.SizeBytes ?? 0) > 0, $"{info?.SizeBytes ?? 0} bytes");

            var restored = service.RestorePreviousBuildAsync().GetAwaiter().GetResult();
            h.Check("the rollback reports the restored version", restored == "b1000", restored);
            h.Check("the kept build is back in use", ReadBuild(install) == "b1000", ReadBuild(install));
            h.Check("the replaced build becomes the new backup", ReadBuild(backup) == "b2000", ReadBuild(backup));
            h.Check("the version of the new backup is recorded",
                service.GetBackupInfo()?.Tag == "b2000", service.GetBackupInfo()?.Tag ?? "null");

            var back = service.RestorePreviousBuildAsync().GetAwaiter().GetResult();
            h.Check("a rollback can be undone the same way",
                back == "b2000" && ReadBuild(install) == "b2000" && ReadBuild(backup) == "b1000", back);

            var noKeep = service.StageForReplace(install, "b2000", keepAsBackup: false);
            h.Check("without the setting the old build is only staged, not kept as backup",
                noKeep.StagedPath != null && !noKeep.StagedAsBackup, noKeep.StagedPath ?? "null");
            WriteBuild(install, "b3000");
            noKeep.Commit();
            h.Check("the staged copy is removed once the extraction succeeded",
                !Directory.Exists(noKeep.StagedPath!), "removed");
            h.Check("the build kept earlier is untouched by a staging elsewhere",
                ReadBuild(backup) == "b1000", ReadBuild(backup));

            h.Check("deleting the kept build clears the rollback",
                service.DeleteBackup() && service.GetBackupInfo() == null, "deleted");

            var custom = Path.Combine(dataDir, "custom");
            WriteBuild(custom, "b4000");
            var customStaging = service.StageForReplace(custom, null, keepAsBackup: true);
            h.Check("a custom folder is not kept as the rollback build",
                customStaging.StagedPath != null && !customStaging.StagedAsBackup && !Directory.Exists(backup),
                customStaging.StagedPath ?? "null");
            customStaging.Rollback();

            h.Check("the launcher's own install directory is recognised",
                service.IsInsideManagedInstall(install), "install");
            h.Check("a folder inside it is recognised too",
                service.IsInsideManagedInstall(Path.Combine(install, "b5000")), "subfolder");
            h.Check("the backup directory is recognised as well",
                service.IsInsideManagedInstall(backup), "backup");
            h.Check("an unrelated folder is left alone",
                !service.IsInsideManagedInstall(custom), "custom");
            h.Check("a folder whose name merely starts the same is not inside it",
                !service.IsInsideManagedInstall(install + "-other"), "sibling");
        }
        finally
        {
            try { Directory.Delete(dataDir, true); } catch { }
        }
    }
}
