using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using LlamaServerLauncher.Models;
using LlamaServerLauncher.Services;

public static class HuggingFaceTests
{
    public static void Run(Harness h)
    {
        RunRefs(h);
        RunSearch(h);
        RunTree(h);
        RunGrouping(h);
        RunResume(h);
        RunPaths(h);
        RunFormatting(h);
        RunQueryKind(h);
        RunLocalState(h);
    }

    private static void RunRefs(Harness h)
    {
        h.Section("HfRepoRef: reading what people paste");

        h.Check("a bare owner/name", Ref("Qwen/Qwen3-8B")?.RepoId == "Qwen/Qwen3-8B",
            Ref("Qwen/Qwen3-8B")?.RepoId ?? "null");
        h.Check("the default branch is main", Ref("Qwen/Qwen3-8B")?.Revision == "main",
            Ref("Qwen/Qwen3-8B")?.Revision ?? "null");

        var page = Ref("https://huggingface.co/unsloth/Qwen3-30B-A3B-GGUF");
        h.Check("the address bar of a repository page", page?.RepoId == "unsloth/Qwen3-30B-A3B-GGUF",
            page?.RepoId ?? "null");

        var noScheme = Ref("huggingface.co/unsloth/Qwen3-30B-A3B-GGUF");
        h.Check("the same link without the scheme", noScheme?.RepoId == "unsloth/Qwen3-30B-A3B-GGUF",
            noScheme?.RepoId ?? "null");

        var tree = Ref("https://huggingface.co/owner/name/tree/main/Q4_K_M");
        h.Check("a folder link keeps the folder", tree?.Subfolder == "Q4_K_M", tree?.Subfolder ?? "null");

        var blob = Ref("https://huggingface.co/owner/name/blob/main/sub/model.gguf");
        h.Check("a link to one file names the folder it sits in",
            blob?.Subfolder == "sub" && blob?.RepoId == "owner/name", blob?.Subfolder ?? "null");

        var resolve = Ref("https://huggingface.co/owner/name/resolve/main/model.gguf?download=true");
        h.Check("a download link is still a repository",
            resolve?.RepoId == "owner/name" && resolve?.Subfolder == "", resolve?.RepoId ?? "null");

        var branch = Ref("https://huggingface.co/owner/name/tree/v2.0");
        h.Check("a branch other than main survives", branch?.Revision == "v2.0", branch?.Revision ?? "null");

        var pr = Ref("https://huggingface.co/owner/name/tree/refs/pr/12/sub");
        h.Check("a pull request ref is three segments, not one",
            pr?.Revision == "refs/pr/12" && pr?.Subfolder == "sub", pr?.Revision ?? "null");

        var shortHost = Ref("https://hf.co/owner/name");
        h.Check("hf.co is the same place", shortHost?.RepoId == "owner/name", shortHost?.RepoId ?? "null");

        h.Check("a link to somebody else's host is refused",
            !HfRepoRef.TryParse("https://evil.example.com/owner/name", null, out _, out var otherErr)
            && otherErr.StartsWith("otherhost", StringComparison.Ordinal), otherErr);
        h.Check("and the refusal names the host", otherErr.Contains("evil.example.com", StringComparison.Ordinal),
            otherErr);

        h.Check("a dataset is not a model repository",
            !HfRepoRef.TryParse("https://huggingface.co/datasets/owner/name", null, out _, out var dsErr)
            && dsErr.StartsWith("notamodel", StringComparison.Ordinal), dsErr);

        h.Check("nothing pasted", !HfRepoRef.TryParse("  ", null, out _, out var emptyErr), emptyErr);
        h.Check("a marker on its own is not a repository",
            !HfRepoRef.TryParse("tree/main", null, out _, out _), "refused");

        h.Check("quotes around a pasted path come off", Ref("\"Qwen/Qwen3-8B\"")?.RepoId == "Qwen/Qwen3-8B",
            Ref("\"Qwen/Qwen3-8B\"")?.RepoId ?? "null");

        h.Section("HfRepoRef: endpoints and urls");

        h.Check("an empty endpoint is the default",
            HfRepoRef.NormaliseEndpoint("") == HfRepoRef.DefaultEndpoint, HfRepoRef.NormaliseEndpoint(""));
        h.Check("a bare host is assumed https",
            HfRepoRef.NormaliseEndpoint("hf-mirror.com") == "https://hf-mirror.com",
            HfRepoRef.NormaliseEndpoint("hf-mirror.com"));
        h.Check("a trailing slash comes off",
            HfRepoRef.NormaliseEndpoint("https://hf-mirror.com/") == "https://hf-mirror.com",
            HfRepoRef.NormaliseEndpoint("https://hf-mirror.com/"));
        h.Check("a half typed scheme does not become a host",
            HfRepoRef.NormaliseEndpoint("https://") == HfRepoRef.DefaultEndpoint,
            HfRepoRef.NormaliseEndpoint("https://"));

        var mirrored = Ref("owner/name", "https://hf-mirror.com");
        h.Check("a mirror is carried on the reference", mirrored?.IsMirrored == true,
            mirrored?.Endpoint ?? "null");
        h.Check("and every url is built from it",
            mirrored?.ResolveUrl("model.gguf") == "https://hf-mirror.com/owner/name/resolve/main/model.gguf",
            mirrored?.ResolveUrl("model.gguf") ?? "null");
        h.Check("a link from the canonical host is still read on a mirror",
            Ref("https://huggingface.co/owner/name", "https://hf-mirror.com")?.RepoId == "owner/name", "ok");

        var plain = Ref("owner/name");
        h.Check("the resolve url",
            plain?.ResolveUrl("Q4/model.gguf") == "https://huggingface.co/owner/name/resolve/main/Q4/model.gguf",
            plain?.ResolveUrl("Q4/model.gguf") ?? "null");
        h.Check("a space in a file name is escaped, the separators are not",
            plain?.ResolveUrl("a b/c d.gguf") == "https://huggingface.co/owner/name/resolve/main/a%20b/c%20d.gguf",
            plain?.ResolveUrl("a b/c d.gguf") ?? "null");
        h.Check("the tree url asks for the whole tree",
            plain?.TreeUrl == "https://huggingface.co/api/models/owner/name/tree/main?recursive=1&limit=1000",
            plain?.TreeUrl ?? "null");
        h.Check("a folder link narrows the tree url",
            tree?.TreeUrl.Contains("/tree/main/Q4_K_M?", StringComparison.Ordinal) == true,
            tree?.TreeUrl ?? "null");

        h.Check("the slug is the shortest text that reads back",
            plain?.Slug == "owner/name" && pr?.Slug == "owner/name/tree/refs/pr/12/sub",
            pr?.Slug ?? "null");
        h.Check("and it leaves the endpoint out", mirrored?.Slug == "owner/name", mirrored?.Slug ?? "null");
    }

    private static void RunSearch(Harness h)
    {
        h.Section("HfApiParser.ParseSearch");

        var json = @"[
          {""id"":""unsloth/Qwen3-GGUF"",""downloads"":12400,""likes"":331,
           ""lastModified"":""2026-08-01T10:00:00.000Z""},
          {""id"":""meta/Llama-GGUF"",""gated"":""auto"",""downloads"":900},
          {""id"":""me/secret"",""private"":true},
          {""modelId"":""old/style""},
          {""downloads"":5}
        ]";
        var repos = HfApiParser.ParseSearch(json);
        h.Check("every named repository is read", repos.Count == 4, repos.Count.ToString(CultureInfo.InvariantCulture));
        h.Check("an entry without an id is dropped", repos.All(r => r.Id.Length > 0), "ok");
        h.Check("the older modelId field is accepted", repos.Any(r => r.Id == "old/style"), "ok");
        h.Check("downloads and likes are read",
            repos[0].Downloads == 12400 && repos[0].Likes == 331, repos[0].Downloads.ToString(CultureInfo.InvariantCulture));
        h.Check("gated as a string counts as gated", repos[1].IsGated, "ok");
        h.Check("private is its own badge",
            repos[2].IsPrivate && repos[2].BadgeText == "private", repos[2].BadgeText);
        h.Check("a plain repository carries no badge", !repos[0].HasBadge, repos[0].BadgeText);
        h.Check("the date is read", repos[0].LastModified?.Year == 2026,
            repos[0].LastModified?.ToString("O", CultureInfo.InvariantCulture) ?? "null");
        h.Check("the stats line reads as one phrase",
            repos[0].StatsText == "12.4k downloads - 331 likes - 2026-08-01", repos[0].StatsText);

        h.Check("malformed json is empty, not an exception",
            HfApiParser.ParseSearch("{not json").Count == 0, "ok");
        h.Check("an object where an array was expected is empty",
            HfApiParser.ParseSearch("{\"a\":1}").Count == 0, "ok");
        h.Check("nothing at all is empty", HfApiParser.ParseSearch(null).Count == 0, "ok");

        h.Section("HfApiParser.NextPageUrl");
        h.Check("the next page is followed",
            HfApiParser.NextPageUrl("<https://huggingface.co/api/x?cursor=abc>; rel=\"next\"")
                == "https://huggingface.co/api/x?cursor=abc",
            HfApiParser.NextPageUrl("<https://huggingface.co/api/x?cursor=abc>; rel=\"next\"") ?? "null");
        h.Check("a last page is not", HfApiParser.NextPageUrl("<https://x>; rel=\"prev\"") == null, "null");
        h.Check("no header at all", HfApiParser.NextPageUrl(null) == null, "null");
    }

    private static void RunTree(Harness h)
    {
        h.Section("HfApiParser.ParseTree");

        var json = @"[
          {""type"":""directory"",""path"":""Q4_K_M"",""size"":0},
          {""type"":""file"",""path"":""README.md"",""size"":1200},
          {""type"":""file"",""path"":""model-Q4_K_M.gguf"",""size"":134,
           ""lfs"":{""oid"":""abc123"",""size"":18000000000}},
          {""type"":""file"",""path"":""small.gguf"",""size"":4096}
        ]";
        var files = HfApiParser.ParseTree(json);
        h.Check("directories are not files", files.All(f => f.Path != "Q4_K_M"),
            files.Count.ToString(CultureInfo.InvariantCulture));
        h.Check("everything else is kept, gguf or not", files.Count == 3,
            files.Count.ToString(CultureInfo.InvariantCulture));

        var lfs = files.First(f => f.Path == "model-Q4_K_M.gguf");
        h.Check("an lfs file reports the model size, not the pointer size",
            lfs.SizeBytes == 18000000000L, lfs.SizeBytes.ToString(CultureInfo.InvariantCulture));
        h.Check("and carries its content hash", lfs.Oid == "abc123" && lfs.IsLfs, lfs.Oid ?? "null");

        var plain = files.First(f => f.Path == "small.gguf");
        h.Check("a plain file keeps its own size", plain.SizeBytes == 4096 && !plain.IsLfs,
            plain.SizeBytes.ToString(CultureInfo.InvariantCulture));

        h.Check("an lfs block without a size falls back to the outer one",
            HfApiParser.ParseTree(@"[{""type"":""file"",""path"":""a.gguf"",""size"":77,""lfs"":{""oid"":""z""}}]")[0]
                .SizeBytes == 77, "77");
        h.Check("malformed json is empty, not an exception", HfApiParser.ParseTree("]").Count == 0, "ok");
    }

    private static void RunGrouping(Harness h)
    {
        h.Section("HfApiParser: shards and quants");

        h.Check("a shard name is read",
            HfApiParser.ParseShard("model-00001-of-00003.gguf") is (string s, 1, 3) && s == "model",
            "ok");
        h.Check("a plain name is not a shard", HfApiParser.ParseShard("model.gguf") == null, "null");
        h.Check("four digits are not the shard pattern",
            HfApiParser.ParseShard("model-0001-of-0003.gguf") == null, "null");

        h.Check("the quant is read off the file name",
            HfApiParser.QuantLabel("Qwen3-30B-Q4_K_M.gguf") == "Q4_K_M",
            HfApiParser.QuantLabel("Qwen3-30B-Q4_K_M.gguf") ?? "null");
        h.Check("and off a shard name too",
            HfApiParser.QuantLabel("Qwen3-IQ2_XXS-00001-of-00003.gguf") == "IQ2_XXS",
            HfApiParser.QuantLabel("Qwen3-IQ2_XXS-00001-of-00003.gguf") ?? "null");
        h.Check("bf16 counts", HfApiParser.QuantLabel("model-BF16.gguf") == "BF16",
            HfApiParser.QuantLabel("model-BF16.gguf") ?? "null");
        h.Check("a name with no quant in it", HfApiParser.QuantLabel("mmproj.gguf") == null, "null");


        h.Check("a folder named after a quant is read as one",
            HfApiParser.QuantFromFolder("Q4_K_M") == "Q4_K_M", HfApiParser.QuantFromFolder("Q4_K_M") ?? "null");
        h.Check("a nested quant folder is read from its last segment",
            HfApiParser.QuantFromFolder("a/b/IQ2_XXS") == "IQ2_XXS",
            HfApiParser.QuantFromFolder("a/b/IQ2_XXS") ?? "null");
        h.Check("an ordinary folder is not a quant",
            HfApiParser.QuantFromFolder("original") == null, HfApiParser.QuantFromFolder("original") ?? "null");
        h.Check("a folder that merely contains a quant word is not one",
            HfApiParser.QuantFromFolder("model-Q4_K_M-files") == null,
            HfApiParser.QuantFromFolder("model-Q4_K_M-files") ?? "null");
        h.Check("no folder at all", HfApiParser.QuantFromFolder("") == null, "null");
        var files = new List<HfRemoteFile>
        {
            new() { Path = "README.md", SizeBytes = 10 },
            new() { Path = "Q4_K_M/model-00002-of-00003.gguf", SizeBytes = 200 },
            new() { Path = "Q4_K_M/model-00001-of-00003.gguf", SizeBytes = 100 },
            new() { Path = "Q4_K_M/model-00003-of-00003.gguf", SizeBytes = 300 },
            new() { Path = "model-Q8_0.gguf", SizeBytes = 900 },
            new() { Path = "mmproj-F16.gguf", SizeBytes = 50 },
        };
        var groups = HfApiParser.GroupQuants("owner/name", "main", files);

        h.Check("non-gguf files are left out", groups.Count == 3,
            groups.Count.ToString(CultureInfo.InvariantCulture));

        var set = groups.First(g => g.IsShardSet);
        h.Check("the parts of one model are one row", set.ShardCount == 3,
            set.ShardCount.ToString(CultureInfo.InvariantCulture));
        h.Check("their sizes add up", set.TotalBytes == 600,
            set.TotalBytes.ToString(CultureInfo.InvariantCulture));
        h.Check("and they are put back in order",
            set.Files[0].FileName.Contains("00001", StringComparison.Ordinal)
            && set.Files[2].FileName.Contains("00003", StringComparison.Ordinal), set.Files[0].FileName);
        h.Check("the row says how many parts there are",
            set.DisplayName.Contains("3 parts", StringComparison.Ordinal), set.DisplayName);
        h.Check("the folder is kept for the dim line", set.SubDir == "Q4_K_M", set.SubDir);
        h.Check("the quant comes off the folder when the file name lacks it",
            set.Quant == "Q4_K_M", set.Quant ?? "null");

        var single = groups.First(g => g.DisplayName == "model-Q8_0.gguf");
        h.Check("a single file is a row of its own",
            !single.IsShardSet && single.TotalBytes == 900, single.TotalBytes.ToString(CultureInfo.InvariantCulture));
        h.Check("its quant is read", single.Quant == "Q8_0", single.Quant ?? "null");

        h.Check("the projector is recognised and sorted last",
            groups[groups.Count - 1].IsProjector, groups[groups.Count - 1].DisplayName);

        var orphan = HfApiParser.GroupQuants("o/n", "main", new List<HfRemoteFile>
        {
            new() { Path = "model-00002-of-00005.gguf", SizeBytes = 5 },
        });
        h.Check("a part with no first part is still a row",
            orphan.Count == 1 && orphan[0].ShardCount == 1, orphan.Count.ToString(CultureInfo.InvariantCulture));

        var sameStem = HfApiParser.GroupQuants("o/n", "main", new List<HfRemoteFile>
        {
            new() { Path = "A/model-00001-of-00002.gguf", SizeBytes = 1 },
            new() { Path = "B/model-00001-of-00002.gguf", SizeBytes = 1 },
        });
        h.Check("the same stem in two folders is two rows", sameStem.Count == 2,
            sameStem.Count.ToString(CultureInfo.InvariantCulture));
    }

    private static void RunResume(Harness h)
    {
        h.Section("HfDownloadPlan: picking up where it stopped");

        var state = new HfPartState { ExpectedSize = 1000, Oid = "abc", Path = "m.gguf" };

        h.Check("nothing on disk starts fresh",
            HfDownloadPlan.DecideResume(0, state, 1000, "abc", out _) == HfResumeDecision.StartFresh, "fresh");
        h.Check("a part with no record of itself starts fresh",
            HfDownloadPlan.DecideResume(400, null, 1000, "abc", out _) == HfResumeDecision.StartFresh, "fresh");

        var resumed = HfDownloadPlan.DecideResume(400, state, 1000, "abc", out long offset);
        h.Check("a part that matches carries on", resumed == HfResumeDecision.Resume, resumed.ToString());
        h.Check("from exactly where it stopped", offset == 400, offset.ToString(CultureInfo.InvariantCulture));

        h.Check("a part the size of the file is done",
            HfDownloadPlan.DecideResume(1000, state, 1000, "abc", out _) == HfResumeDecision.AlreadyComplete, "done");
        h.Check("a part longer than the file is wrong",
            HfDownloadPlan.DecideResume(1400, state, 1000, "abc", out _) == HfResumeDecision.Conflict, "conflict");
        h.Check("a different file under the same name is wrong",
            HfDownloadPlan.DecideResume(400, state, 1000, "zzz", out _) == HfResumeDecision.Conflict, "conflict");
        h.Check("a changed size is wrong",
            HfDownloadPlan.DecideResume(400, state, 2000, "abc", out _) == HfResumeDecision.Conflict, "conflict");
        h.Check("an unknown hash on either side is not a conflict",
            HfDownloadPlan.DecideResume(400, new HfPartState { ExpectedSize = 1000 }, 1000, null, out _)
                == HfResumeDecision.Resume, "resume");

        h.Section("HfDownloadPlan: reading the response");

        var range = HfDownloadPlan.ParseContentRange("bytes 1024-2047/4096");
        h.Check("a content range is read",
            range?.Start == 1024 && range?.End == 2047 && range?.Total == 4096, range?.Total.ToString() ?? "null");
        h.Check("an unsatisfied range has no span",
            HfDownloadPlan.ParseContentRange("bytes */4096") == null, "null");
        h.Check("garbage is not a range", HfDownloadPlan.ParseContentRange("nonsense") == null, "null");
        h.Check("no header is not a range", HfDownloadPlan.ParseContentRange(null) == null, "null");

        h.Check("206 at the asked-for offset is a resume",
            HfDownloadPlan.RangeHonored(206, "bytes 400-999/1000", 400), "ok");
        h.Check("200 means the server ignored the range",
            !HfDownloadPlan.RangeHonored(200, null, 400), "restart");
        h.Check("206 somewhere else is not our resume",
            !HfDownloadPlan.RangeHonored(206, "bytes 0-999/1000", 400), "restart");
        h.Check("a fresh download needs no range at all",
            HfDownloadPlan.RangeHonored(200, null, 0), "ok");

        h.Check("the whole size comes from the content range",
            HfDownloadPlan.TotalFromResponse("bytes 400-999/1000", 600, 400) == 1000, "1000");
        h.Check("without one, content-length is only the rest",
            HfDownloadPlan.TotalFromResponse(null, 600, 400) == 1000, "1000");
        h.Check("and with neither, the size is unknown",
            HfDownloadPlan.TotalFromResponse(null, null, 400) == null, "null");

        h.Section("HfDownloadPlan: where the token may go");

        var origin = new Uri("https://huggingface.co/owner/name/resolve/main/m.gguf");
        h.Check("the token stays on the host it was meant for",
            HfDownloadPlan.ShouldForwardAuth(origin, new Uri("https://huggingface.co/elsewhere")), "ok");
        h.Check("and does not follow a redirect to the cdn",
            !HfDownloadPlan.ShouldForwardAuth(origin, new Uri("https://cdn-lfs.huggingface.co/repos/x?sig=y")),
            "dropped");
        h.Check("nor to any other host",
            !HfDownloadPlan.ShouldForwardAuth(origin, new Uri("https://evil.example.com/x")), "dropped");
        h.Check("nor down to plain http",
            !HfDownloadPlan.ShouldForwardAuth(origin, new Uri("http://huggingface.co/x")), "dropped");
    }

    private static void RunPaths(Harness h)
    {
        h.Section("HfDownloadPlan: names and places");

        var root = Path.Combine(Path.GetTempPath(), "hf-plan-tests");

        h.Check("an ordinary name lands under the folder",
            HfDownloadPlan.TrySafeDestination(root, "model.gguf", out var ok)
            && ok == Path.Combine(root, "model.gguf"), ok);
        h.Check("a subfolder from the repository is kept",
            HfDownloadPlan.TrySafeDestination(root, "Q4/model.gguf", out var sub)
            && sub == Path.Combine(root, "Q4", "model.gguf"), sub);
        h.Check("climbing out is refused",
            !HfDownloadPlan.TrySafeDestination(root, "../evil.gguf", out _), "refused");
        h.Check("climbing out from inside is refused",
            !HfDownloadPlan.TrySafeDestination(root, "a/../../evil.gguf", out _), "refused");
        h.Check("a backslash is a separator too, not an escape",
            !HfDownloadPlan.TrySafeDestination(root, "..\\evil.gguf", out _), "refused");
        h.Check("an empty name is refused", !HfDownloadPlan.TrySafeDestination(root, "", out _), "refused");

        h.Check("a repository becomes one folder name",
            HfDownloadPlan.RepoFolderName("unsloth/Qwen3-30B-A3B-GGUF") == "unsloth_Qwen3-30B-A3B-GGUF",
            HfDownloadPlan.RepoFolderName("unsloth/Qwen3-30B-A3B-GGUF"));
        h.Check("an empty repository still has a name",
            HfDownloadPlan.RepoFolderName("") == "huggingface", HfDownloadPlan.RepoFolderName(""));

        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(root, "model.gguf"),
        };
        var second = HfDownloadPlan.ResolveTargetPath(root, "model.gguf", taken.Contains);
        h.Check("a name already in use gets a suffix",
            second == Path.Combine(root, "model (2).gguf"), second);
        h.Check("a free name is left alone",
            HfDownloadPlan.ResolveTargetPath(root, "other.gguf", taken.Contains)
                == Path.Combine(root, "other.gguf"), "ok");

        taken.Add(HfDownloadPlan.PartPathFor(Path.Combine(root, "half.gguf")));
        h.Check("a half downloaded name is in use too",
            HfDownloadPlan.ResolveTargetPath(root, "half.gguf", taken.Contains)
                == Path.Combine(root, "half (2).gguf"), "ok");

        h.Check("the part file sits beside the final one",
            HfDownloadPlan.PartPathFor("C:\\m\\a.gguf") == "C:\\m\\a.gguf.part",
            HfDownloadPlan.PartPathFor("C:\\m\\a.gguf"));
        h.Check("and so does its record",
            HfDownloadPlan.StatePathFor("C:\\m\\a.gguf") == "C:\\m\\a.gguf.part.json",
            HfDownloadPlan.StatePathFor("C:\\m\\a.gguf"));

        h.Check("a very long path is spotted",
            HfDownloadPlan.IsPathTooLong(new string('a', 300)), "too long");
        h.Check("an ordinary one is not",
            !HfDownloadPlan.IsPathTooLong(Path.Combine(root, "model.gguf")), "fine");

        h.Section("HfDownloadPlan: free space");

        h.Check("the margin is a quarter gigabyte on a small file",
            HfDownloadPlan.RequiredFreeBytes(1000) == 1000 + HfDownloadPlan.MinFreeMarginBytes,
            HfDownloadPlan.RequiredFreeBytes(1000).ToString(CultureInfo.InvariantCulture));
        h.Check("and one percent on a large one",
            HfDownloadPlan.RequiredFreeBytes(100_000_000_000L) == 101_000_000_000L,
            HfDownloadPlan.RequiredFreeBytes(100_000_000_000L).ToString(CultureInfo.InvariantCulture));
        h.Check("nothing needed, nothing checked", HfDownloadPlan.RequiredFreeBytes(0) == 0, "0");
        h.Check("a download of nothing always fits",
            HfDownloadPlan.HasEnoughFreeSpace(Path.GetTempPath(), 0, out _), "ok");
        h.Check("a download of every byte on earth does not",
            !HfDownloadPlan.HasEnoughFreeSpace(Path.GetTempPath(), long.MaxValue / 2, out _), "refused");
        h.Check("an unreachable folder does not block the download",
            HfDownloadPlan.HasEnoughFreeSpace("\\\\nosuchhost\\share", 1000, out _), "allowed");
    }

    private static void RunFormatting(Harness h)
    {
        h.Section("HfFormatting");

        h.Check("thousands", HfFormatting.Count(12400) == "12.4k", HfFormatting.Count(12400));
        h.Check("millions", HfFormatting.Count(2_500_000) == "2.5M", HfFormatting.Count(2_500_000));
        h.Check("small numbers are left alone", HfFormatting.Count(42) == "42", HfFormatting.Count(42));

        h.Check("a speed reads per second",
            HfFormatting.Speed(42.1 * 1024 * 1024).EndsWith("/s", StringComparison.Ordinal),
            HfFormatting.Speed(42.1 * 1024 * 1024));
        h.Check("no speed yet, nothing said", HfFormatting.Speed(0) == "", "empty");

        h.Check("minutes and seconds", HfFormatting.Eta(TimeSpan.FromSeconds(724)) == "12:04",
            HfFormatting.Eta(TimeSpan.FromSeconds(724)));
        h.Check("hours when there are hours",
            HfFormatting.Eta(TimeSpan.FromSeconds(3731)) == "1:02:11",
            HfFormatting.Eta(TimeSpan.FromSeconds(3731)));
        h.Check("an unknown time is not guessed", HfFormatting.Eta(null) == "", "empty");
    }


    private static void RunQueryKind(Harness h)
    {
        h.Section("HfRepoRef: a repository or words to search for");

        h.Check("owner/name is a reference", HfRepoRef.LooksLikeRef("Qwen/Qwen3-8B"), "ref");
        h.Check("so is a full link",
            HfRepoRef.LooksLikeRef("https://huggingface.co/unsloth/Qwen3-30B-A3B-GGUF"), "ref");
        h.Check("and a link to a file inside it",
            HfRepoRef.LooksLikeRef("huggingface.co/unsloth/Model-GGUF/blob/main/Q4_K_M.gguf"), "ref");

        h.Check("two words are a search, not a repository",
            !HfRepoRef.LooksLikeRef("qwen gguf"), "search");
        h.Check("one word is a search too, even though it parses as a repository id",
            !HfRepoRef.LooksLikeRef("qwen"), "search");
        h.Check("a slash with a space around it is still a search",
            !HfRepoRef.LooksLikeRef("qwen / gguf"), "search");
        h.Check("nothing typed is neither", !HfRepoRef.LooksLikeRef("   "), "neither");
        h.Check("quotes around a pasted reference are ignored",
            HfRepoRef.LooksLikeRef("\"Qwen/Qwen3-8B\""), "ref");
    }

    private static void RunLocalState(Harness h)
    {
        h.Section("HfDownloadPlan.Inspect: what is already on disk");

        var files = new List<HfRemoteFile>
        {
            new() { Path = "model-00001-of-00002.gguf", SizeBytes = 1000 },
            new() { Path = "model-00002-of-00002.gguf", SizeBytes = 500 },
        };

        var nothing = HfDownloadPlan.Inspect("d", files, _ => -1);
        h.Check("an empty folder is neither complete nor partial",
            !nothing.Complete && !nothing.Partial && nothing.HaveBytes == 0, "empty");

        var whole = HfDownloadPlan.Inspect("d", files, p => p.EndsWith("00001-of-00002.gguf") ? 1000
            : p.EndsWith("00002-of-00002.gguf") ? 500 : -1);
        h.Check("both parts at their full size means complete", whole.Complete, "complete");
        h.Check("and every byte is accounted for", whole.HaveBytes == 1500,
            whole.HaveBytes.ToString(CultureInfo.InvariantCulture));

        var halfway = HfDownloadPlan.Inspect("d", files, p => p.EndsWith("00001-of-00002.gguf") ? 1000
            : p.EndsWith("00002-of-00002.gguf.part") ? 200 : -1);
        h.Check("one part done and one in progress is partial",
            !halfway.Complete && halfway.Partial, "partial");
        h.Check("the partial byte count includes the .part file", halfway.HaveBytes == 1200,
            halfway.HaveBytes.ToString(CultureInfo.InvariantCulture));

        var wrongSize = HfDownloadPlan.Inspect("d", files, p => p.EndsWith("00001-of-00002.gguf") ? 999
            : p.EndsWith("00002-of-00002.gguf") ? 500 : -1);
        h.Check("a file of the wrong size does not count as downloaded",
            !wrongSize.Complete, "not complete");
        h.Check("and its bytes are not claimed either, there is no .part to resume from",
            wrongSize.HaveBytes == 500, wrongSize.HaveBytes.ToString(CultureInfo.InvariantCulture));

        var oversized = HfDownloadPlan.Inspect("d", files.Take(1).ToList(),
            p => p.EndsWith(".part") ? 4000 : -1);
        h.Check("a .part longer than the file cannot report more than the whole",
            oversized.HaveBytes == 1000, oversized.HaveBytes.ToString(CultureInfo.InvariantCulture));

        var noFolder = HfDownloadPlan.Inspect("", files, _ => 1000);
        h.Check("with no target folder chosen nothing is claimed",
            !noFolder.Complete && noFolder.HaveBytes == 0, "nothing");

        var entry = new HfQuantEntry
        {
            RepoId = "unsloth/Model-GGUF",
            DisplayName = "Q4_K_M",
            Files = files,
            TotalBytes = 1500,
        };
        var changed = new List<string>();
        entry.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? "");
        entry.SetLocal(false, 1200);
        h.Check("the list is told when a file turns out to be partially downloaded",
            changed.Contains("LocalText") && changed.Contains("HasLocalState"),
            string.Join(",", changed));
        h.Check("and the badge says how much is there", entry.LocalText.Contains("1.2 KB"),
            entry.LocalText);

        changed.Clear();
        entry.SetLocal(false, 1200);
        h.Check("setting the same state again says nothing", changed.Count == 0,
            changed.Count.ToString(CultureInfo.InvariantCulture));

        entry.SetFit(VramFit.Tight, 6_000_000_000);
        h.Check("a vram verdict shows up as a badge", entry.FitsTight && entry.FitText.StartsWith("VRAM "),
            entry.FitText);
        h.Check("and only one of the three colours is on",
            !entry.FitsEasily && !entry.FitsNot, "one");
    }
    private static HfRepoRef? Ref(string text, string? endpoint = null) =>
        HfRepoRef.TryParse(text, endpoint, out var result, out _) ? result : null;
}
