using System;
using System.IO;
using System.Text;
using LlamaServerLauncher.Services;

public static class GgufMetadataTests
{
    public static void Run(Harness h)
    {
        h.Section("GgufMetadataService.TryReadMaxContext");

        var basic = BuildGguf(3, 4, w =>
        {
            WriteKvString(w, "general.architecture", "llama");
            WriteKvString(w, "general.name", "test");
            WriteKvUint32(w, "llama.context_length", 4096);
            WriteKvUint32(w, "llama.block_count", 32);
        });
        h.Check("basic uint32 ctx", Read(basic) == 4096, Read(basic)?.ToString() ?? "null");

        var afterArray = BuildGguf(3, 4, w =>
        {
            WriteKvStringArray(w, "tokenizer.ggml.tokens", new[] { "a", "bb", "ccc" });
            WriteKvString(w, "general.architecture", "qwen2");
            WriteKvFloat32(w, "some.scale", 1.5f);
            WriteKvUint64(w, "qwen2.context_length", 32768);
        });
        h.Check("uint64 ctx after string array + float", Read(afterArray) == 32768, Read(afterArray)?.ToString() ?? "null");

        var numArray = BuildGguf(3, 3, w =>
        {
            WriteKvString(w, "general.architecture", "llama");
            WriteKvUint32Array(w, "some.arr", new uint[] { 1, 2, 3, 4 });
            WriteKvUint32(w, "llama.context_length", 2048);
        });
        h.Check("ctx after fixed-size array", Read(numArray) == 2048, Read(numArray)?.ToString() ?? "null");

        var fallback = BuildGguf(3, 2, w =>
        {
            WriteKvString(w, "general.architecture", "foo");
            WriteKvUint32(w, "bar.context_length", 8192);
        });
        h.Check("fallback when arch key absent", Read(fallback) == 8192, Read(fallback)?.ToString() ?? "null");

        var noArch = BuildGguf(3, 1, w => WriteKvUint32(w, "mistral.context_length", 16384));
        h.Check("single ctx no arch", Read(noArch) == 16384, Read(noArch)?.ToString() ?? "null");

        var noCtx = BuildGguf(3, 1, w => WriteKvString(w, "general.architecture", "llama"));
        h.Check("no context_length -> null", Read(noCtx) == null, Read(noCtx)?.ToString() ?? "null");

        var v1 = BuildGguf(1, 1, w => WriteKvUint32(w, "llama.context_length", 4096));
        h.Check("version 1 -> null", Read(v1) == null, Read(v1)?.ToString() ?? "null");

        var badMagic = new byte[] { 0x11, 0x22, 0x33, 0x44, 0, 0, 0, 3 };
        h.Check("bad magic -> null", Read(badMagic) == null, Read(badMagic)?.ToString() ?? "null");

        h.Check("empty stream -> null", Read(Array.Empty<byte>()) == null, "ok");

        h.Section("GgufMetadataService.TryRead");

        var moe = BuildGguf(3, 7, w =>
        {
            WriteKvString(w, "general.architecture", "qwen3moe");
            WriteKvString(w, "general.size_label", "30B-A3B");
            WriteKvUint32(w, "general.file_type", 15);
            WriteKvUint32(w, "qwen3moe.context_length", 40960);
            WriteKvUint32(w, "qwen3moe.block_count", 48);
            WriteKvUint32(w, "qwen3moe.expert_count", 128);
            WriteKvString(w, "tokenizer.chat_template", "{{ bos }}{% for m in messages %}{{ m }}{% endfor %}");
        });
        var moeInfo = ReadInfo(moe);
        h.Check("moe: quant", moeInfo?.Quant == "Q4_K_M", moeInfo?.Quant ?? "null");
        h.Check("moe: size label", moeInfo?.SizeLabel == "30B-A3B", moeInfo?.SizeLabel ?? "null");
        h.Check("moe: max context", moeInfo?.MaxContext == 40960, moeInfo?.MaxContext?.ToString() ?? "null");
        h.Check("moe: block count", moeInfo?.BlockCount == 48, moeInfo?.BlockCount?.ToString() ?? "null");
        h.Check("moe: expert count", moeInfo?.ExpertCount == 128, moeInfo?.ExpertCount?.ToString() ?? "null");
        h.Check("moe: IsMoe", moeInfo?.IsMoe == true, (moeInfo?.IsMoe)?.ToString() ?? "null");
        h.Check("moe: has chat template", moeInfo?.HasChatTemplate == true, (moeInfo?.HasChatTemplate)?.ToString() ?? "null");
        h.Check("moe: not projector", moeInfo?.IsProjector == false, (moeInfo?.IsProjector)?.ToString() ?? "null");

        var dense = BuildGguf(3, 2, w =>
        {
            WriteKvString(w, "general.architecture", "llama");
            WriteKvUint32(w, "llama.block_count", 32);
        });
        var denseInfo = ReadInfo(dense);
        h.Check("dense: block count", denseInfo?.BlockCount == 32, denseInfo?.BlockCount?.ToString() ?? "null");
        h.Check("dense: not MoE", denseInfo?.IsMoe == false, (denseInfo?.IsMoe)?.ToString() ?? "null");
        h.Check("dense: quant null", denseInfo?.Quant == null, denseInfo?.Quant ?? "null");
        h.Check("dense: no chat template", denseInfo?.HasChatTemplate == false, (denseInfo?.HasChatTemplate)?.ToString() ?? "null");

        var projector = BuildGguf(3, 2, w =>
        {
            WriteKvString(w, "general.architecture", "clip");
            WriteKvUint32(w, "clip.vision.block_count", 24);
        });
        var projInfo = ReadInfo(projector);
        h.Check("projector: IsProjector", projInfo?.IsProjector == true, (projInfo?.IsProjector)?.ToString() ?? "null");
        h.Check("projector: HasVision", projInfo?.HasVision == true, (projInfo?.HasVision)?.ToString() ?? "null");

        var unknownFtype = BuildGguf(3, 1, w => WriteKvUint32(w, "general.file_type", 999));
        h.Check("unknown ftype fallback", ReadInfo(unknownFtype)?.Quant == "ftype999", ReadInfo(unknownFtype)?.Quant ?? "null");

        RunGeometry(h);
        RunTensors(h);
        RunShards(h);
    }

    private static void RunGeometry(Harness h)
    {
        h.Section("GgufMetadataService: attention geometry");

        var geometry = BuildGguf(3, 10, w =>
        {
            WriteKvString(w, "general.architecture", "llama");
            WriteKvUint32(w, "llama.block_count", 32);
            WriteKvUint32(w, "llama.embedding_length", 4096);
            WriteKvUint32(w, "llama.attention.head_count", 32);
            WriteKvUint32(w, "llama.attention.head_count_kv", 8);
            WriteKvUint32(w, "llama.attention.key_length", 128);
            WriteKvUint32(w, "llama.attention.value_length", 128);
            WriteKvUint32(w, "llama.rope.dimension_count", 128);
            WriteKvUint32(w, "llama.attention.sliding_window", 4096);
            WriteKvStringArray(w, "tokenizer.ggml.tokens", new[] { "a", "b", "c" });
        });
        var geo = ReadInfo(geometry);
        h.Check("embedding length", geo?.EmbeddingLength == 4096, geo?.EmbeddingLength?.ToString() ?? "null");
        h.Check("head count", geo?.HeadCount == 32, geo?.HeadCount?.ToString() ?? "null");
        h.Check("kv heads are not taken for attention heads",
            geo?.HeadCountKv == 8, geo?.HeadCountKv?.ToString() ?? "null");
        h.Check("key and value length", geo?.KeyLength == 128 && geo?.ValueLength == 128,
            $"{geo?.KeyLength}/{geo?.ValueLength}");
        h.Check("rope dimension count", geo?.RopeDimensionCount == 128, geo?.RopeDimensionCount?.ToString() ?? "null");
        h.Check("sliding window", geo?.SlidingWindow == 4096, geo?.SlidingWindow?.ToString() ?? "null");
        h.Check("vocabulary counted from the token list", geo?.VocabSize == 3, geo?.VocabSize?.ToString() ?? "null");

        var reversed = BuildGguf(3, 3, w =>
        {
            WriteKvString(w, "general.architecture", "gemma3");
            WriteKvUint32(w, "gemma3.attention.head_count_kv", 4);
            WriteKvUint32(w, "gemma3.attention.head_count", 16);
        });
        var rev = ReadInfo(reversed);
        h.Check("key order does not decide which head count wins",
            rev?.HeadCount == 16 && rev?.HeadCountKv == 4, $"{rev?.HeadCount}/{rev?.HeadCountKv}");

        var mla = BuildGguf(3, 3, w =>
        {
            WriteKvString(w, "general.architecture", "deepseek2");
            WriteKvUint32(w, "deepseek2.attention.kv_lora_rank", 512);
            WriteKvUint32(w, "deepseek2.expert_used_count", 8);
        });
        var mlaInfo = ReadInfo(mla);
        h.Check("compressed kv rank", mlaInfo?.KvLoraRank == 512, mlaInfo?.KvLoraRank?.ToString() ?? "null");
        h.Check("experts used per token", mlaInfo?.ExpertUsedCount == 8, mlaInfo?.ExpertUsedCount?.ToString() ?? "null");

        var experts = BuildGguf(3, 3, w =>
        {
            WriteKvString(w, "general.architecture", "qwen3moe");
            WriteKvUint32(w, "qwen3moe.expert_count", 128);
            WriteKvUint32(w, "qwen3moe.expert_used_count", 8);
        });
        var expertInfo = ReadInfo(experts);
        h.Check("total experts stay separate from used experts",
            expertInfo?.ExpertCount == 128 && expertInfo?.ExpertUsedCount == 8,
            $"{expertInfo?.ExpertCount}/{expertInfo?.ExpertUsedCount}");
    }

    private static void RunTensors(Harness h)
    {
        h.Section("GgufMetadataService: tensor table");

        var model = BuildGguf(3, 3, w =>
        {
            WriteKvString(w, "general.architecture", "qwen3moe");
            WriteKvUint32(w, "qwen3moe.block_count", 2);
            WriteKvUint32(w, "qwen3moe.expert_count", 128);
        }, 6, w =>
        {
            WriteTensor(w, "token_embd.weight", 1, 4, 8);            // F16: 32 elements, 64 bytes
            WriteTensor(w, "blk.0.attn_q.weight", 12, 256, 2);       // Q4_K: 2 blocks, 288 bytes
            WriteTensor(w, "blk.0.ffn_up_exps.weight", 12, 256, 4);  // Q4_K: 4 blocks, 576 bytes
            WriteTensor(w, "blk.1.ffn_up_exps.weight", 12, 256, 4);  // Q4_K: 4 blocks, 576 bytes
            WriteTensor(w, "output.weight", 14, 256, 1);             // Q6_K: 1 block, 210 bytes
            WriteTensor(w, "rope_freqs.weight", 250, 10);            // type nobody knows
        });

        h.Check("the quick read does not touch the tensor table",
            ReadInfo(model)?.Tensors == null, "not read");

        var t = ReadDetailed(model)?.Tensors;
        h.Check("every tensor is accounted for", t?.TensorCount == 6, t?.TensorCount.ToString() ?? "null");
        h.Check("total weight size", t?.TotalBytes == 1714, t?.TotalBytes.ToString() ?? "null");
        h.Check("embeddings are kept apart", t?.EmbeddingBytes == 64, t?.EmbeddingBytes.ToString() ?? "null");
        h.Check("the output head is kept apart", t?.OutputBytes == 210, t?.OutputBytes.ToString() ?? "null");
        h.Check("repeating blocks are summed", t?.RepeatingBytes == 1440, t?.RepeatingBytes.ToString() ?? "null");
        h.Check("expert weights are summed", t?.ExpertBytes == 1152, t?.ExpertBytes.ToString() ?? "null");
        h.Check("a tensor of an unknown type adds nothing",
            t?.OtherBytes == 0, t?.OtherBytes.ToString() ?? "null");
        h.Check("blocks are counted from the tensor names", t?.BlockCount == 2, t?.BlockCount.ToString() ?? "null");
        h.Check("attention and experts of one block are added together",
            t?.BytesForBlock(0) == 864, t?.BytesForBlock(0).ToString() ?? "null");
        h.Check("the second block carries only its experts",
            t?.BytesForBlock(1) == 576 && t?.ExpertBytesForBlock(1) == 576,
            $"{t?.BytesForBlock(1)}/{t?.ExpertBytesForBlock(1)}");
        h.Check("a block outside the model reports nothing",
            t?.BytesForBlock(7) == 0 && t?.BytesForBlock(-1) == 0, "0");

        var shared = BuildGguf(3, 2, w =>
        {
            WriteKvString(w, "general.architecture", "qwen3moe");
            WriteKvUint32(w, "qwen3moe.block_count", 1);
        }, 3, w =>
        {
            WriteTensor(w, "blk.0.ffn_gate_inp.weight", 0, 8, 2);    // F32 router: 64 bytes
            WriteTensor(w, "blk.0.ffn_up_shexp.weight", 1, 8, 4);    // F16 shared expert: 64 bytes
            WriteTensor(w, "blk.0.ffn_up_exps.weight", 1, 8, 4);     // F16 experts: 64 bytes
        });
        var sh = ReadDetailed(shared)?.Tensors;
        h.Check("the router and the shared expert are not offloadable experts",
            sh?.ExpertBytes == 64 && sh?.BytesForBlock(0) == 192,
            $"{sh?.ExpertBytes}/{sh?.BytesForBlock(0)}");

        var hybrid = BuildGguf(3, 2, w =>
        {
            WriteKvString(w, "general.architecture", "qwen3next");
            WriteKvUint32(w, "qwen3next.block_count", 3);
        }, 5, w =>
        {
            WriteTensor(w, "blk.0.attn_norm.weight", 0, 8);      // a recurrent block carries this too
            WriteTensor(w, "blk.0.ssm_in.weight", 1, 8, 8);
            WriteTensor(w, "blk.1.attn_k.weight", 1, 8, 8);
            WriteTensor(w, "blk.1.attn_v.weight", 1, 8, 8);
            WriteTensor(w, "blk.2.attn_qkv.weight", 1, 8, 24);   // one fused projection
        });
        var hy = ReadDetailed(hybrid)?.Tensors;
        h.Check("a recurrent block holds no kv cache",
            hy?.BlockHasKv(0) == false, (hy?.BlockHasKv(0))?.ToString() ?? "null");
        h.Check("an attention block does",
            hy?.BlockHasKv(1) == true, (hy?.BlockHasKv(1))?.ToString() ?? "null");
        h.Check("a fused qkv projection counts as attention too",
            hy?.BlockHasKv(2) == true, (hy?.BlockHasKv(2))?.ToString() ?? "null");
        h.Check("only the attention blocks are counted",
            hy?.KvBlockCount == 2, hy?.KvBlockCount.ToString() ?? "null");

        var noTensors = BuildGguf(3, 1, w => WriteKvString(w, "general.architecture", "llama"));
        h.Check("a header without tensors reports none", ReadDetailed(noTensors)?.Tensors == null, "null");

        var merged = GgufTensorSummary.Merge(
            new GgufTensorSummary
            {
                TensorCount = 2, TotalBytes = 100, RepeatingBytes = 100,
                BlockBytes = new long[] { 60, 40 }, BlockExpertBytes = new long[] { 0, 40 },
                BlockKvBytes = new long[] { 20, 0 }
            },
            new GgufTensorSummary
            {
                TensorCount = 1, TotalBytes = 30, RepeatingBytes = 30,
                BlockBytes = new long[] { 0, 0, 30 }, BlockExpertBytes = new long[] { 0, 0, 30 },
                BlockKvBytes = new long[] { 0, 0, 10 }
            });
        h.Check("parts of a split model add up",
            merged?.TensorCount == 3 && merged?.TotalBytes == 130, merged?.TotalBytes.ToString() ?? "null");
        h.Check("blocks from a later part keep their own index",
            merged?.BlockCount == 3 && merged?.BytesForBlock(1) == 40 && merged?.BytesForBlock(2) == 30,
            $"{merged?.BytesForBlock(1)}/{merged?.BytesForBlock(2)}");
        h.Check("attention blocks from every part are seen",
            merged?.KvBlockCount == 2, merged?.KvBlockCount.ToString() ?? "null");
        h.Check("merging with nothing keeps what there is",
            GgufTensorSummary.Merge(null, merged) == merged && GgufTensorSummary.Merge(merged, null) == merged,
            "unchanged");
    }

    private static void RunShards(Harness h)
    {
        h.Section("GgufMetadataService: split models");

        var incomplete = BuildGguf(3, 2, w =>
        {
            WriteKvString(w, "general.architecture", "llama");
            WriteKvUint32(w, "split.count", 3);
        });
        var part = ReadDetailed(incomplete);
        h.Check("a split model knows how many parts it has",
            part?.SplitCount == 3, part?.SplitCount?.ToString() ?? "null");
        h.Check("one part out of three is not the whole model",
            part?.IsSplitComplete == false, (part?.IsSplitComplete)?.ToString() ?? "null");

        var whole = ReadDetailed(BuildGguf(3, 1, w => WriteKvString(w, "general.architecture", "llama")));
        h.Check("a single file model is complete as it is",
            whole?.IsSplitComplete == true, (whole?.IsSplitComplete)?.ToString() ?? "null");

        var dir = Path.Combine(Path.GetTempPath(), "gguf-shard-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var first = Path.Combine(dir, "model-00001-of-00003.gguf");
            File.WriteAllBytes(first, Array.Empty<byte>());
            File.WriteAllBytes(Path.Combine(dir, "model-00002-of-00003.gguf"), Array.Empty<byte>());

            var shards = GgufMetadataService.EnumerateShards(first);
            h.Check("the parts lying next to each other are found", shards.Count == 2, shards.Count.ToString());
            h.Check("they are listed in order",
                shards[0].EndsWith("00001-of-00003.gguf", StringComparison.Ordinal)
                && shards[1].EndsWith("00002-of-00003.gguf", StringComparison.Ordinal),
                Path.GetFileName(shards[0]));

            var fromSecond = GgufMetadataService.EnumerateShards(Path.Combine(dir, "model-00002-of-00003.gguf"));
            h.Check("any part leads to the whole set", fromSecond.Count == 2, fromSecond.Count.ToString());

            var plain = Path.Combine(dir, "model.gguf");
            File.WriteAllBytes(plain, Array.Empty<byte>());
            var single = GgufMetadataService.EnumerateShards(plain);
            h.Check("a plain file is its own only part",
                single.Count == 1 && single[0] == plain, single.Count.ToString());

            var missing = Path.Combine(dir, "absent-00001-of-00002.gguf");
            var none = GgufMetadataService.EnumerateShards(missing);
            h.Check("a name that promises parts nobody has still points at itself",
                none.Count == 1 && none[0] == missing, none.Count.ToString());
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    private static int? Read(byte[] bytes) => GgufMetadataService.TryReadMaxContext(new MemoryStream(bytes));

    private static GgufModelInfo? ReadInfo(byte[] bytes) => GgufMetadataService.TryRead(new MemoryStream(bytes));

    private static GgufModelInfo? ReadDetailed(byte[] bytes) =>
        GgufMetadataService.TryReadDetailed(new MemoryStream(bytes));

    private static byte[] BuildGguf(uint version, ulong kvCount, Action<BinaryWriter> kvs,
        ulong tensorCount = 0, Action<BinaryWriter>? tensors = null)
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write((uint)0x46554747);
        w.Write(version);
        w.Write(tensorCount);
        w.Write(kvCount);
        kvs(w);
        tensors?.Invoke(w);
        w.Flush();
        return ms.ToArray();
    }

    private static void WriteTensor(BinaryWriter w, string name, uint type, params ulong[] dims)
    {
        WriteStr(w, name);
        w.Write((uint)dims.Length);
        foreach (var d in dims) w.Write(d);
        w.Write(type);
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

    private static void WriteKvUint64(BinaryWriter w, string key, ulong val)
    {
        WriteStr(w, key);
        w.Write((uint)10);
        w.Write(val);
    }

    private static void WriteKvFloat32(BinaryWriter w, string key, float val)
    {
        WriteStr(w, key);
        w.Write((uint)6);
        w.Write(val);
    }

    private static void WriteKvStringArray(BinaryWriter w, string key, string[] vals)
    {
        WriteStr(w, key);
        w.Write((uint)9);
        w.Write((uint)8);
        w.Write((ulong)vals.Length);
        foreach (var v in vals) WriteStr(w, v);
    }

    private static void WriteKvUint32Array(BinaryWriter w, string key, uint[] vals)
    {
        WriteStr(w, key);
        w.Write((uint)9);
        w.Write((uint)4);
        w.Write((ulong)vals.Length);
        foreach (var v in vals) w.Write(v);
    }
}
