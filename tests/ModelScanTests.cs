using System;
using System.IO;
using System.Linq;
using System.Text;
using LlamaServerLauncher.Models;
using LlamaServerLauncher.Services;

public static class ModelScanTests
{
    public static void Run(Harness h)
    {
        h.Section("ModelScanFormatting.FormatSize");
        h.Check("zero", ModelScanFormatting.FormatSize(0) == "0 B", ModelScanFormatting.FormatSize(0));
        h.Check("bytes", ModelScanFormatting.FormatSize(512) == "512 B", ModelScanFormatting.FormatSize(512));
        h.Check("kb", ModelScanFormatting.FormatSize(1536) == "1.5 KB", ModelScanFormatting.FormatSize(1536));
        h.Check("mb", ModelScanFormatting.FormatSize(5 * 1024 * 1024) == "5.0 MB", ModelScanFormatting.FormatSize(5 * 1024 * 1024));
        h.Check("gb", ModelScanFormatting.FormatSize(1024L * 1024 * 1024) == "1.0 GB", ModelScanFormatting.FormatSize(1024L * 1024 * 1024));
        h.Check("negative", ModelScanFormatting.FormatSize(-10) == "0 B", ModelScanFormatting.FormatSize(-10));

        h.Section("ModelScanFormatting.IsNonFirstShard");
        h.Check("first shard included", ModelScanFormatting.IsNonFirstShard("m-00001-of-00003.gguf") == false, "ok");
        h.Check("second shard skipped", ModelScanFormatting.IsNonFirstShard("m-00002-of-00003.gguf") == true, "ok");
        h.Check("third shard skipped", ModelScanFormatting.IsNonFirstShard("m-00003-of-00003.gguf") == true, "ok");
        h.Check("plain file not a shard", ModelScanFormatting.IsNonFirstShard("model.gguf") == false, "ok");
        h.Check("empty not a shard", ModelScanFormatting.IsNonFirstShard("") == false, "ok");

        h.Section("ModelScanFormatting.BuildMeta");
        h.Check("null info -> empty", ModelScanFormatting.BuildMeta(null) == "", "ok");
        var moe = new GgufModelInfo { Quant = "Q4_K_M", SizeLabel = "30B-A3B", ExpertCount = 128, MaxContext = 40960, HasVision = true };
        var moeMeta = ModelScanFormatting.BuildMeta(moe);
        h.Check("moe has quant", moeMeta.Contains("Q4_K_M"), moeMeta);
        h.Check("moe has size label", moeMeta.Contains("30B-A3B"), moeMeta);
        h.Check("moe has MoE·128", moeMeta.Contains("MoE·128"), moeMeta);
        h.Check("moe has ctx", moeMeta.Contains("ctx 40960"), moeMeta);
        h.Check("moe has vision", moeMeta.Contains("vision"), moeMeta);
        var dense = new GgufModelInfo { Quant = "Q8_0", ExpertCount = 1 };
        h.Check("dense not MoE", !ModelScanFormatting.BuildMeta(dense).Contains("MoE"), ModelScanFormatting.BuildMeta(dense));

        h.Section("ModelScanService.Scan");
        var root = Path.Combine(Path.GetTempPath(), "lsl-scan-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            WriteGgufFile(Path.Combine(root, "model-a.gguf"), 3, w =>
            {
                WriteKvString(w, "general.architecture", "llama");
                WriteKvUint32(w, "general.file_type", 15);
                WriteKvUint32(w, "llama.context_length", 4096);
            });
            WriteGgufFile(Path.Combine(root, "model-b.gguf"), 2, w =>
            {
                WriteKvString(w, "general.architecture", "qwen3moe");
                WriteKvUint32(w, "qwen3moe.expert_count", 128);
            });
            WriteGgufFile(Path.Combine(root, "big-00001-of-00003.gguf"), 1, w =>
                WriteKvString(w, "general.architecture", "llama"));
            WriteGgufFile(Path.Combine(root, "big-00002-of-00003.gguf"), 1, w =>
                WriteKvString(w, "general.architecture", "llama"));
            WriteGgufFile(Path.Combine(root, "truncated.gguf"), 1,
                w => WriteKvString(w, "general.architecture", "llama"), tensorCount: 5);
            WriteGgufFile(Path.Combine(root, "weighed.gguf"), 8, w =>
            {
                WriteKvString(w, "general.architecture", "llama");
                WriteKvUint32(w, "llama.block_count", 1);
                WriteKvUint32(w, "llama.context_length", 4096);
                WriteKvUint32(w, "llama.embedding_length", 1024);
                WriteKvUint32(w, "llama.attention.head_count", 8);
                WriteKvUint32(w, "llama.attention.head_count_kv", 8);
                WriteKvUint32(w, "llama.attention.key_length", 128);
                WriteKvUint32(w, "llama.attention.value_length", 128);
            }, tensorCount: 2, tensors: w =>
            {
                WriteTensor(w, "blk.0.attn_k.weight", 1024, 1024);
                WriteTensor(w, "blk.0.ffn_gate.weight", 1024, 1024);
            });
            File.WriteAllText(Path.Combine(root, "notes.txt"), "ignore me");

            var sub = Path.Combine(root, "sub");
            Directory.CreateDirectory(sub);
            WriteGgufFile(Path.Combine(sub, "model-c.gguf"), 1, w =>
                WriteKvString(w, "general.architecture", "gemma"));

            WriteGgufFile(Path.Combine(root, "mmproj-a.gguf"), 2, w =>
            {
                WriteKvString(w, "general.architecture", "clip");
                WriteKvUint32(w, "clip.vision.block_count", 24);
            });

            var flat = ModelScanService.Scan(root, false);
            h.Check("flat count = 5", flat.Count == 5, flat.Count.ToString());
            h.Check("flat skips shard 00002", flat.All(m => m.FileName != "big-00002-of-00003.gguf"), "ok");
            h.Check("flat keeps shard 00001", flat.Any(m => m.FileName == "big-00001-of-00003.gguf"), "ok");
            h.Check("flat ignores .txt", flat.All(m => m.FileName.EndsWith(".gguf")), "ok");
            h.Check("flat excludes subfolder", flat.All(m => m.FileName != "model-c.gguf"), "ok");

            h.Check("a projector is not a model to run",
                flat.All(m => m.FileName != "mmproj-a.gguf"), "hidden");

            var projectors = ModelScanService.Scan(root, false, null, default, ModelScanKind.Projectors);
            h.Check("and the projector scan finds only it",
                projectors.Count == 1 && projectors[0].FileName == "mmproj-a.gguf",
                projectors.Count + " found");
            h.Check("a projector is marked as one",
                projectors[0].IsProjector, "marked");

            var a = flat.FirstOrDefault(m => m.FileName == "model-a.gguf");
            h.Check("model-a read", a != null, a == null ? "null" : "ok");
            h.Check("model-a ctx", a?.Info?.MaxContext == 4096, a?.Info?.MaxContext?.ToString() ?? "null");
            h.Check("model-a quant", a?.Info?.Quant == "Q4_K_M", a?.Info?.Quant ?? "null");
            h.Check("model-a size > 0", (a?.SizeBytes ?? 0) > 0, (a?.SizeBytes ?? 0).ToString());

            var split = flat.FirstOrDefault(m => m.FileName == "big-00001-of-00003.gguf");
            long shards = new FileInfo(Path.Combine(root, "big-00001-of-00003.gguf")).Length
                + new FileInfo(Path.Combine(root, "big-00002-of-00003.gguf")).Length;
            h.Check("a split model is as big as all of its shards, not just the first",
                split?.SizeBytes == shards, $"{split?.SizeBytes}/{shards}");

            var b = flat.FirstOrDefault(m => m.FileName == "model-b.gguf");
            h.Check("model-b is MoE", b?.Info?.IsMoe == true, (b?.Info?.IsMoe)?.ToString() ?? "null");

            var rec = ModelScanService.Scan(root, true);
            h.Check("recursive count = 6", rec.Count == 6, rec.Count.ToString());
            var c = rec.FirstOrDefault(m => m.FileName == "model-c.gguf");
            h.Check("recursive finds subfolder", c != null, c == null ? "null" : "ok");
            h.Check("recursive relDir = sub", c?.RelativeDir == "sub", c?.RelativeDir ?? "null");

            var truncated = flat.FirstOrDefault(m => m.FileName == "truncated.gguf");
            h.Check("a tensor table that ends early does not take the metadata with it",
                truncated?.Info?.Architecture == "llama", truncated?.Info?.Architecture ?? "null");
            h.Check("and leaves no tensor summary behind",
                truncated?.Info?.Tensors == null, truncated?.Info?.Tensors == null ? "null" : "present");

            var weighed = flat.FirstOrDefault(m => m.FileName == "weighed.gguf");
            h.Check("the scan reads the tensor table, not just the keys",
                weighed?.Info?.Tensors?.BlockCount == 1,
                weighed?.Info?.Tensors?.BlockCount.ToString() ?? "null");
            h.Check("without a budget nothing is judged",
                weighed?.Fit == VramFit.Unknown, (weighed?.Fit)?.ToString() ?? "null");

            var judged = ModelScanService.Scan(root, false,
                new VramBudget { Config = new ServerConfiguration(), AvailableBytes = 8L * 1024 * 1024 * 1024 });
            var fitted = judged.FirstOrDefault(m => m.FileName == "weighed.gguf");
            h.Check("a budget turns the scan into a verdict",
                fitted?.Fit == VramFit.Fits, (fitted?.Fit)?.ToString() ?? "null");
            h.Check("and the row can say what it would take",
                fitted != null && fitted.FitBytes > 0 && fitted.FitText.StartsWith("VRAM "),
                fitted?.FitText ?? "null");
            h.Check("a file with no tensors gets no verdict even with a budget",
                judged.First(m => m.FileName == "model-a.gguf").Fit == VramFit.Unknown, "unknown");

            h.Check("missing folder -> empty", ModelScanService.Scan(Path.Combine(root, "nope"), false).Count == 0, "ok");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static void WriteGgufFile(string path, ulong kvCount, Action<BinaryWriter> kvs,
        ulong tensorCount = 0, Action<BinaryWriter>? tensors = null)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var w = new BinaryWriter(fs);
        w.Write((uint)0x46554747);
        w.Write((uint)3);
        w.Write(tensorCount);
        w.Write(kvCount);
        kvs(w);
        tensors?.Invoke(w);
    }

    private static void WriteTensor(BinaryWriter w, string name, params ulong[] dims)
    {
        WriteStr(w, name);
        w.Write((uint)dims.Length);
        foreach (var d in dims) w.Write(d);
        w.Write((uint)1);
        w.Write((ulong)0);
    }

    private static void WriteStr(BinaryWriter w, string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        w.Write((ulong)bytes.Length);
        w.Write(bytes);
    }

    private static void WriteKvString(BinaryWriter w, string key, string val)
    {
        WriteStr(w, key);
        w.Write((uint)8);
        WriteStr(w, val);
    }

    private static void WriteKvUint32(BinaryWriter w, string key, uint val)
    {
        WriteStr(w, key);
        w.Write((uint)4);
        w.Write(val);
    }
}
