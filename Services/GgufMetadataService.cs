using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace LlamaServerLauncher.Services;

public sealed record GgufModelInfo
{
    public int? MaxContext { get; init; }
    public string? Architecture { get; init; }
    public int? BlockCount { get; init; }
    public int? ExpertCount { get; init; }
    public int? ExpertUsedCount { get; init; }
    public int? EmbeddingLength { get; init; }
    public int? HeadCount { get; init; }
    public int? HeadCountKv { get; init; }
    public int? KeyLength { get; init; }
    public int? ValueLength { get; init; }
    public int? KeyLengthSwa { get; init; }
    public int? ValueLengthSwa { get; init; }
    public int? FeedForwardLength { get; init; }
    public int? ExpertFeedForwardLength { get; init; }

    public IReadOnlyList<int>? HeadCountKvPerLayer { get; init; }
    public IReadOnlyList<bool>? SlidingWindowPattern { get; init; }
    public int? RopeDimensionCount { get; init; }
    public int? KvLoraRank { get; init; }
    public int? SlidingWindow { get; init; }
    public int? VocabSize { get; init; }
    public int? SplitCount { get; init; }
    public int ShardsRead { get; init; } = 1;
    public string? Quant { get; init; }
    public string? SizeLabel { get; init; }
    public string? Name { get; init; }
    public bool HasChatTemplate { get; init; }
    public bool IsProjector { get; init; }
    public bool HasVision { get; init; }
    public GgufTensorSummary? Tensors { get; init; }

    public bool IsMoe => ExpertCount is int e && e > 1;

    public bool IsSplitComplete => SplitCount is not int n || n <= 1 || ShardsRead >= n;
}

public sealed class GgufTensorSummary
{
    public int TensorCount { get; init; }
    public long TotalBytes { get; init; }
    public long EmbeddingBytes { get; init; }
    public long OutputBytes { get; init; }
    public long RepeatingBytes { get; init; }
    public long ExpertBytes { get; init; }
    public long OtherBytes { get; init; }
    public IReadOnlyList<long> BlockBytes { get; init; } = Array.Empty<long>();
    public IReadOnlyList<long> BlockExpertBytes { get; init; } = Array.Empty<long>();

    public IReadOnlyList<long> BlockKvBytes { get; init; } = Array.Empty<long>();

    public int BlockCount => BlockBytes.Count;

    public int KvBlockCount
    {
        get
        {
            int count = 0;
            foreach (var bytes in BlockKvBytes)
                if (bytes > 0) count++;
            return count;
        }
    }

    public long BytesForBlock(int index) =>
        index >= 0 && index < BlockBytes.Count ? BlockBytes[index] : 0;

    public long ExpertBytesForBlock(int index) =>
        index >= 0 && index < BlockExpertBytes.Count ? BlockExpertBytes[index] : 0;

    public bool BlockHasKv(int index) =>
        index >= 0 && index < BlockKvBytes.Count && BlockKvBytes[index] > 0;

    public static GgufTensorSummary? Merge(GgufTensorSummary? a, GgufTensorSummary? b)
    {
        if (a == null) return b;
        if (b == null) return a;
        return new GgufTensorSummary
        {
            TensorCount = a.TensorCount + b.TensorCount,
            TotalBytes = a.TotalBytes + b.TotalBytes,
            EmbeddingBytes = a.EmbeddingBytes + b.EmbeddingBytes,
            OutputBytes = a.OutputBytes + b.OutputBytes,
            RepeatingBytes = a.RepeatingBytes + b.RepeatingBytes,
            ExpertBytes = a.ExpertBytes + b.ExpertBytes,
            OtherBytes = a.OtherBytes + b.OtherBytes,
            BlockBytes = AddPerIndex(a.BlockBytes, b.BlockBytes),
            BlockExpertBytes = AddPerIndex(a.BlockExpertBytes, b.BlockExpertBytes),
            BlockKvBytes = AddPerIndex(a.BlockKvBytes, b.BlockKvBytes),
        };
    }

    private static IReadOnlyList<long> AddPerIndex(IReadOnlyList<long> a, IReadOnlyList<long> b)
    {
        int count = Math.Max(a.Count, b.Count);
        if (count == 0) return Array.Empty<long>();
        var result = new long[count];
        for (int i = 0; i < count; i++)
            result[i] = (i < a.Count ? a[i] : 0) + (i < b.Count ? b[i] : 0);
        return result;
    }
}

public static class GgufMetadataService
{
    private const uint Magic = 0x46554747;
    private const int MaxBlockIndex = 4096;
    private const ulong MaxTensorCount = 500_000;
    private const ulong MaxPerLayerValues = 8192;
    private const ulong MaxElements = 1UL << 50;

    private static readonly Regex ShardPattern =
        new(@"^(?<stem>.+)-(?<no>\d{5})-of-(?<total>\d{5})\.gguf$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static int? TryReadMaxContext(string path) => TryRead(path)?.MaxContext;

    public static int? TryReadMaxContext(Stream stream) => TryRead(stream)?.MaxContext;

    public static GgufModelInfo? TryRead(string path) => ReadFile(path, includeTensors: false);

    public static GgufModelInfo? TryRead(Stream stream) => TryReadCore(stream, includeTensors: false);

    public static GgufModelInfo? TryReadDetailed(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;

        GgufModelInfo? head = null;
        GgufTensorSummary? tensors = null;
        int read = 0;

        foreach (var shard in EnumerateShards(path))
        {
            var info = ReadFile(shard, includeTensors: true);
            if (info == null) continue;
            head ??= info;
            tensors = GgufTensorSummary.Merge(tensors, info.Tensors);
            read++;
        }

        if (head == null) return null;
        return head with { Tensors = tensors, ShardsRead = read };
    }

    public static GgufModelInfo? TryReadDetailed(Stream stream) => TryReadCore(stream, includeTensors: true);

    public static IReadOnlyList<string> EnumerateShards(string path)
    {
        var single = new[] { path };
        try
        {
            var match = ShardPattern.Match(Path.GetFileName(path));
            if (!match.Success) return single;

            if (!int.TryParse(match.Groups["total"].Value, NumberStyles.None,
                    CultureInfo.InvariantCulture, out int total) || total < 1 || total > 999)
                return single;

            var dir = Path.GetDirectoryName(path) ?? "";
            var stem = match.Groups["stem"].Value;
            var found = new List<string>(total);
            for (int i = 1; i <= total; i++)
            {
                var shard = Path.Combine(dir, string.Format(CultureInfo.InvariantCulture,
                    "{0}-{1:00000}-of-{2:00000}.gguf", stem, i, total));
                if (File.Exists(shard)) found.Add(shard);
            }
            return found.Count > 0 ? found : single;
        }
        catch
        {
            return single;
        }
    }

    private static GgufModelInfo? ReadFile(string path, bool includeTensors)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16);
            return TryReadCore(fs, includeTensors);
        }
        catch
        {
            return null;
        }
    }

    private static GgufModelInfo? TryReadCore(Stream stream, bool includeTensors)
    {
        try
        {
            using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            if (r.ReadUInt32() != Magic) return null;
            uint version = r.ReadUInt32();
            if (version < 2) return null;
            ulong tensorCount = r.ReadUInt64();
            ulong kvCount = r.ReadUInt64();

            string? arch = null;
            string? sizeLabel = null;
            string? name = null;
            bool hasChatTemplate = false;
            bool sawClipKey = false;
            bool hasVision = false;
            long? tokenCount = null;
            var ints = new List<KeyValuePair<string, long>>();
            var arrays = new List<KeyValuePair<string, List<long>>>();

            for (ulong i = 0; i < kvCount; i++)
            {
                string key = ReadGgufString(r);

                if (key.StartsWith("clip.", StringComparison.Ordinal)) sawClipKey = true;
                if (key.Contains(".vision.", StringComparison.Ordinal)) hasVision = true;

                uint type = r.ReadUInt32();
                if (type == TypeString)
                {
                    string val = ReadGgufString(r);
                    switch (key)
                    {
                        case "general.architecture": arch = val; break;
                        case "general.size_label": sizeLabel = val; break;
                        case "general.name": name = val; break;
                        case "tokenizer.chat_template": hasChatTemplate = true; break;
                    }
                }
                else if (type == TypeArray)
                {
                    var sink = WantsPerLayerValues(key) ? new List<long>() : null;
                    ulong count = ReadArray(r, sink);
                    if (key == "tokenizer.ggml.tokens" && count <= int.MaxValue)
                        tokenCount = (long)count;
                    if (sink is { Count: > 0 })
                        arrays.Add(new KeyValuePair<string, List<long>>(key, sink));
                }
                else if (IsInteger(type))
                {
                    ints.Add(new KeyValuePair<string, long>(key, ReadInteger(r, type)));
                }
                else
                {
                    SkipScalar(r, type);
                }
            }

            GgufTensorSummary? tensors = includeTensors ? ReadTensors(r, tensorCount) : null;

            bool isProjector = string.Equals(arch, "clip", StringComparison.Ordinal) || sawClipKey;
            long? fileType = Exact(ints, "general.file_type");

            return new GgufModelInfo
            {
                MaxContext = ToPositiveInt(Pick(ints, arch, "context_length")),
                Architecture = arch,
                BlockCount = ToPositiveInt(Pick(ints, arch, "block_count")),
                ExpertCount = ToPositiveInt(Pick(ints, arch, "expert_count")),
                ExpertUsedCount = ToPositiveInt(Pick(ints, arch, "expert_used_count")),
                EmbeddingLength = ToPositiveInt(Pick(ints, arch, "embedding_length")),
                HeadCount = ToPositiveInt(Pick(ints, arch, "head_count")),
                HeadCountKv = ToPositiveInt(Pick(ints, arch, "head_count_kv")),
                KeyLength = ToPositiveInt(Pick(ints, arch, "key_length")),
                ValueLength = ToPositiveInt(Pick(ints, arch, "value_length")),
                KeyLengthSwa = ToPositiveInt(Pick(ints, arch, "key_length_swa")),
                ValueLengthSwa = ToPositiveInt(Pick(ints, arch, "value_length_swa")),
                FeedForwardLength = ToPositiveInt(Pick(ints, arch, "feed_forward_length")),
                ExpertFeedForwardLength = ToPositiveInt(Pick(ints, arch, "expert_feed_forward_length")),
                HeadCountKvPerLayer = ToIntList(PickArray(arrays, arch, "head_count_kv")),
                SlidingWindowPattern = ToBoolList(PickArray(arrays, arch, "sliding_window_pattern")),
                RopeDimensionCount = ToPositiveInt(Pick(ints, arch, "rope.dimension_count")),
                KvLoraRank = ToPositiveInt(Pick(ints, arch, "kv_lora_rank")),
                SlidingWindow = ToPositiveInt(Pick(ints, arch, "sliding_window")),
                VocabSize = ToPositiveInt(Pick(ints, arch, "vocab_size") ?? tokenCount),
                SplitCount = ToPositiveInt(Exact(ints, "split.count")),
                Quant = fileType is long ft ? QuantName(ft) : null,
                SizeLabel = string.IsNullOrWhiteSpace(sizeLabel) ? null : sizeLabel,
                Name = string.IsNullOrWhiteSpace(name) ? null : name,
                HasChatTemplate = hasChatTemplate,
                IsProjector = isProjector,
                HasVision = hasVision,
                Tensors = tensors,
            };
        }
        catch
        {
            return null;
        }
    }

    private static GgufTensorSummary? ReadTensors(BinaryReader r, ulong tensorCount)
    {
        if (tensorCount == 0 || tensorCount > MaxTensorCount) return null;

        var acc = new TensorAccumulator();
        for (ulong i = 0; i < tensorCount; i++)
        {
            string name = ReadGgufString(r);
            uint dimCount = r.ReadUInt32();
            if (dimCount > 4) return null;

            ulong elements = 1;
            for (uint d = 0; d < dimCount; d++)
            {
                ulong dim = r.ReadUInt64();
                if (dim == 0 || dim > MaxElements) return null;
                elements *= dim;
                if (elements > MaxElements) return null;
            }

            uint type = r.ReadUInt32();
            r.ReadUInt64();
            acc.Add(name, TensorBytes(elements, type));
        }
        return acc.Build();
    }

    private static long TensorBytes(ulong elements, uint type)
    {
        var (blockSize, typeSize) = GgmlTypeSize(type);
        if (blockSize <= 0) return 0;
        return (long)(elements / (ulong)blockSize * (ulong)typeSize);
    }

    private sealed class TensorAccumulator
    {
        private readonly List<long> _blocks = new();
        private readonly List<long> _blockExperts = new();
        private readonly List<long> _blockKv = new();
        private int _count;
        private long _total, _embedding, _output, _repeating, _expert, _other;

        public void Add(string name, long bytes)
        {
            _count++;
            _total += bytes;

            int block = ParseBlock(name, out string role);
            if (block >= 0)
            {
                _repeating += bytes;
                Grow(_blocks, block);
                _blocks[block] += bytes;
                if (role.Contains("_exps", StringComparison.Ordinal))
                {
                    _expert += bytes;
                    Grow(_blockExperts, block);
                    _blockExperts[block] += bytes;
                }
                if (IsKvProjection(role))
                {
                    Grow(_blockKv, block);
                    _blockKv[block] += bytes;
                }
            }
            else if (name.StartsWith("token_embd", StringComparison.Ordinal))
            {
                _embedding += bytes;
            }
            else if (name.StartsWith("output", StringComparison.Ordinal))
            {
                _output += bytes;
            }
            else
            {
                _other += bytes;
            }
        }

        public GgufTensorSummary Build() => new()
        {
            TensorCount = _count,
            TotalBytes = _total,
            EmbeddingBytes = _embedding,
            OutputBytes = _output,
            RepeatingBytes = _repeating,
            ExpertBytes = _expert,
            OtherBytes = _other,
            BlockBytes = _blocks.ToArray(),
            BlockExpertBytes = _blockExperts.ToArray(),
            BlockKvBytes = _blockKv.ToArray(),
        };

        private static void Grow(List<long> list, int index)
        {
            while (list.Count <= index) list.Add(0);
        }
    }

    private static bool IsKvProjection(string role) =>
        role.StartsWith("attn_k", StringComparison.Ordinal)
        || role.StartsWith("attn_v", StringComparison.Ordinal)
        || role.StartsWith("attn_qkv", StringComparison.Ordinal);

    private static int ParseBlock(string name, out string role)
    {
        role = "";
        const string prefix = "blk.";
        if (!name.StartsWith(prefix, StringComparison.Ordinal)) return -1;

        int i = prefix.Length;
        int value = 0;
        int digits = 0;
        while (i < name.Length && name[i] >= '0' && name[i] <= '9')
        {
            value = value * 10 + (name[i] - '0');
            if (value > MaxBlockIndex) return -1;
            i++;
            digits++;
        }
        if (digits == 0 || i >= name.Length || name[i] != '.') return -1;

        role = name.Substring(i + 1);
        return value;
    }

    // block size in elements, bytes per block; unknown types report nothing
    private static (int Block, int Size) GgmlTypeSize(uint type) => type switch
    {
        0 => (1, 4),        // F32
        1 => (1, 2),        // F16
        2 => (32, 18),      // Q4_0
        3 => (32, 20),      // Q4_1
        6 => (32, 22),      // Q5_0
        7 => (32, 24),      // Q5_1
        8 => (32, 34),      // Q8_0
        9 => (32, 36),      // Q8_1
        10 => (256, 84),    // Q2_K
        11 => (256, 110),   // Q3_K
        12 => (256, 144),   // Q4_K
        13 => (256, 176),   // Q5_K
        14 => (256, 210),   // Q6_K
        15 => (256, 292),   // Q8_K
        16 => (256, 66),    // IQ2_XXS
        17 => (256, 74),    // IQ2_XS
        18 => (256, 98),    // IQ3_XXS
        19 => (256, 50),    // IQ1_S
        20 => (32, 18),     // IQ4_NL
        21 => (256, 110),   // IQ3_S
        22 => (256, 82),    // IQ2_S
        23 => (256, 136),   // IQ4_XS
        24 => (1, 1),       // I8
        25 => (1, 2),       // I16
        26 => (1, 4),       // I32
        27 => (1, 8),       // I64
        28 => (1, 8),       // F64
        29 => (256, 56),    // IQ1_M
        30 => (1, 2),       // BF16
        34 => (256, 54),    // TQ1_0
        35 => (256, 66),    // TQ2_0
        39 => (32, 17),     // MXFP4
        _ => (0, 0)
    };

    private static long? Pick(List<KeyValuePair<string, long>> ints, string? arch, string suffix)
    {
        string tail = "." + suffix;
        if (arch != null)
        {
            string head = arch + ".";
            foreach (var kv in ints)
                if (kv.Key.StartsWith(head, StringComparison.Ordinal)
                    && kv.Key.EndsWith(tail, StringComparison.Ordinal))
                    return kv.Value;
        }
        foreach (var kv in ints)
            if (kv.Key.EndsWith(tail, StringComparison.Ordinal))
                return kv.Value;
        return null;
    }

    private static long? Exact(List<KeyValuePair<string, long>> ints, string key)
    {
        foreach (var kv in ints)
            if (kv.Key == key)
                return kv.Value;
        return null;
    }

    private static int? ToPositiveInt(long? value) =>
        value is long v && v > 0 && v <= int.MaxValue ? (int)v : null;

    private const uint TypeUint8 = 0, TypeInt8 = 1, TypeUint16 = 2, TypeInt16 = 3,
        TypeUint32 = 4, TypeInt32 = 5, TypeFloat32 = 6, TypeBool = 7, TypeString = 8,
        TypeArray = 9, TypeUint64 = 10, TypeInt64 = 11, TypeFloat64 = 12;

    private static bool IsInteger(uint type) =>
        type is TypeUint8 or TypeInt8 or TypeUint16 or TypeInt16
             or TypeUint32 or TypeInt32 or TypeUint64 or TypeInt64;

    private static long ReadInteger(BinaryReader r, uint type) => type switch
    {
        TypeUint8 => r.ReadByte(),
        TypeInt8 => r.ReadSByte(),
        TypeUint16 => r.ReadUInt16(),
        TypeInt16 => r.ReadInt16(),
        TypeUint32 => r.ReadUInt32(),
        TypeInt32 => r.ReadInt32(),
        TypeUint64 => (long)r.ReadUInt64(),
        TypeInt64 => r.ReadInt64(),
        _ => throw new InvalidDataException()
    };

    private static void SkipScalar(BinaryReader r, uint type) =>
        r.BaseStream.Seek(FixedSize(type), SeekOrigin.Current);

    private static bool WantsPerLayerValues(string key) =>
        key.EndsWith(".attention.head_count_kv", StringComparison.Ordinal)
        || key.EndsWith(".attention.sliding_window_pattern", StringComparison.Ordinal);

    private static ulong ReadArray(BinaryReader r, List<long>? sink)
    {
        uint elemType = r.ReadUInt32();
        ulong count = r.ReadUInt64();

        bool collect = sink != null && count <= MaxPerLayerValues
            && (IsInteger(elemType) || elemType == TypeBool);

        if (collect)
        {
            for (ulong i = 0; i < count; i++)
                sink!.Add(elemType == TypeBool ? r.ReadByte() : ReadInteger(r, elemType));
        }
        else if (elemType == TypeString)
        {
            for (ulong i = 0; i < count; i++)
            {
                ulong len = r.ReadUInt64();
                r.BaseStream.Seek((long)len, SeekOrigin.Current);
            }
        }
        else if (elemType == TypeArray)
        {
            for (ulong i = 0; i < count; i++) ReadArray(r, null);
        }
        else
        {
            r.BaseStream.Seek((long)count * FixedSize(elemType), SeekOrigin.Current);
        }
        return count;
    }

    private static List<long>? PickArray(List<KeyValuePair<string, List<long>>> arrays, string? arch, string suffix)
    {
        string tail = "." + suffix;
        if (arch != null)
        {
            string head = arch + ".";
            foreach (var kv in arrays)
                if (kv.Key.StartsWith(head, StringComparison.Ordinal)
                    && kv.Key.EndsWith(tail, StringComparison.Ordinal))
                    return kv.Value;
        }
        foreach (var kv in arrays)
            if (kv.Key.EndsWith(tail, StringComparison.Ordinal))
                return kv.Value;
        return null;
    }

    private static IReadOnlyList<int>? ToIntList(List<long>? values)
    {
        if (values == null || values.Count == 0) return null;
        var result = new int[values.Count];
        for (int i = 0; i < values.Count; i++)
            result[i] = values[i] > 0 && values[i] <= int.MaxValue ? (int)values[i] : 0;
        return result;
    }

    private static IReadOnlyList<bool>? ToBoolList(List<long>? values)
    {
        if (values == null || values.Count == 0) return null;
        var result = new bool[values.Count];
        for (int i = 0; i < values.Count; i++)
            result[i] = values[i] != 0;
        return result;
    }

    private static int FixedSize(uint type) => type switch
    {
        TypeUint8 or TypeInt8 or TypeBool => 1,
        TypeUint16 or TypeInt16 => 2,
        TypeUint32 or TypeInt32 or TypeFloat32 => 4,
        TypeUint64 or TypeInt64 or TypeFloat64 => 8,
        _ => throw new InvalidDataException($"Unknown GGUF type {type}")
    };

    private static string ReadGgufString(BinaryReader r)
    {
        ulong len = r.ReadUInt64();
        if (len > int.MaxValue) throw new InvalidDataException("GGUF string too long");
        byte[] bytes = r.ReadBytes((int)len);
        return Encoding.UTF8.GetString(bytes);
    }

    private static string QuantName(long fileType) => fileType switch
    {
        0 => "F32",
        1 => "F16",
        2 => "Q4_0",
        3 => "Q4_1",
        7 => "Q8_0",
        8 => "Q5_0",
        9 => "Q5_1",
        10 => "Q2_K",
        11 => "Q3_K_S",
        12 => "Q3_K_M",
        13 => "Q3_K_L",
        14 => "Q4_K_S",
        15 => "Q4_K_M",
        16 => "Q5_K_S",
        17 => "Q5_K_M",
        18 => "Q6_K",
        19 => "IQ2_XXS",
        20 => "IQ2_XS",
        21 => "Q2_K_S",
        22 => "IQ3_XS",
        23 => "IQ3_XXS",
        24 => "IQ1_S",
        25 => "IQ4_NL",
        26 => "IQ3_S",
        27 => "IQ3_M",
        28 => "IQ2_S",
        29 => "IQ2_M",
        30 => "IQ4_XS",
        31 => "IQ1_M",
        32 => "BF16",
        36 => "TQ1_0",
        37 => "TQ2_0",
        _ => $"ftype{fileType}"
    };
}
