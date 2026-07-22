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
    }

    private static int? Read(byte[] bytes) => GgufMetadataService.TryReadMaxContext(new MemoryStream(bytes));

    private static GgufModelInfo? ReadInfo(byte[] bytes) => GgufMetadataService.TryRead(new MemoryStream(bytes));

    private static byte[] BuildGguf(uint version, ulong kvCount, Action<BinaryWriter> kvs)
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write((uint)0x46554747);
        w.Write(version);
        w.Write((ulong)0);
        w.Write(kvCount);
        kvs(w);
        w.Flush();
        return ms.ToArray();
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
