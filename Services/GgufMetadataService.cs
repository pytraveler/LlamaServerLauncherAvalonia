using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LlamaServerLauncher.Services;

public sealed class GgufModelInfo
{
    public int? MaxContext { get; init; }
    public string? Architecture { get; init; }
    public int? BlockCount { get; init; }
    public int? ExpertCount { get; init; }
    public string? Quant { get; init; }
    public string? SizeLabel { get; init; }
    public string? Name { get; init; }
    public bool HasChatTemplate { get; init; }
    public bool IsProjector { get; init; }
    public bool HasVision { get; init; }

    public bool IsMoe => ExpertCount is int e && e > 1;
}

public static class GgufMetadataService
{
    private const uint Magic = 0x46554747;

    public static int? TryReadMaxContext(string path) => TryRead(path)?.MaxContext;

    public static int? TryReadMaxContext(Stream stream) => TryRead(stream)?.MaxContext;

    public static GgufModelInfo? TryRead(string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16);
            return TryRead(fs);
        }
        catch
        {
            return null;
        }
    }

    public static GgufModelInfo? TryRead(Stream stream)
    {
        try
        {
            using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            if (r.ReadUInt32() != Magic) return null;
            uint version = r.ReadUInt32();
            if (version < 2) return null;
            r.ReadUInt64();
            ulong kvCount = r.ReadUInt64();

            string? arch = null;
            string? sizeLabel = null;
            string? name = null;
            long? fileType = null;
            bool hasChatTemplate = false;
            bool sawClipKey = false;
            bool hasVision = false;
            var contextLengths = new Dictionary<string, long>(StringComparer.Ordinal);
            var blockCounts = new Dictionary<string, long>(StringComparer.Ordinal);
            var expertCounts = new Dictionary<string, long>(StringComparer.Ordinal);

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
                    SkipArray(r);
                }
                else if (IsInteger(type))
                {
                    long val = ReadInteger(r, type);
                    if (key.EndsWith(".context_length", StringComparison.Ordinal))
                        contextLengths[key] = val;
                    else if (key.EndsWith(".block_count", StringComparison.Ordinal))
                        blockCounts[key] = val;
                    else if (key.EndsWith(".expert_count", StringComparison.Ordinal))
                        expertCounts[key] = val;
                    else if (key == "general.file_type")
                        fileType = val;
                }
                else
                {
                    SkipScalar(r, type);
                }
            }

            bool isProjector = string.Equals(arch, "clip", StringComparison.Ordinal) || sawClipKey;

            return new GgufModelInfo
            {
                MaxContext = ToPositiveInt(PickForArch(contextLengths, arch)),
                Architecture = arch,
                BlockCount = ToPositiveInt(PickForArch(blockCounts, arch)),
                ExpertCount = ToPositiveInt(PickForArch(expertCounts, arch)),
                Quant = fileType is long ft ? QuantName(ft) : null,
                SizeLabel = string.IsNullOrWhiteSpace(sizeLabel) ? null : sizeLabel,
                Name = string.IsNullOrWhiteSpace(name) ? null : name,
                HasChatTemplate = hasChatTemplate,
                IsProjector = isProjector,
                HasVision = hasVision,
            };
        }
        catch
        {
            return null;
        }
    }

    private static long? PickForArch(Dictionary<string, long> values, string? arch)
    {
        if (values.Count == 0) return null;
        if (arch != null)
        {
            foreach (var kv in values)
                if (kv.Key.StartsWith(arch + ".", StringComparison.Ordinal))
                    return kv.Value;
        }
        foreach (var kv in values) return kv.Value;
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

    private static void SkipArray(BinaryReader r)
    {
        uint elemType = r.ReadUInt32();
        ulong count = r.ReadUInt64();
        if (elemType == TypeString)
        {
            for (ulong i = 0; i < count; i++)
            {
                ulong len = r.ReadUInt64();
                r.BaseStream.Seek((long)len, SeekOrigin.Current);
            }
        }
        else if (elemType == TypeArray)
        {
            for (ulong i = 0; i < count; i++) SkipArray(r);
        }
        else
        {
            r.BaseStream.Seek((long)count * FixedSize(elemType), SeekOrigin.Current);
        }
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
