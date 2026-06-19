using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LlamaServerLauncher.Optimization.Distributions;

[JsonConverter(typeof(DistributionJsonConverter))]
public abstract class Distribution
{
    public abstract double ToInternalRepr(object value);

    public abstract object ToExternalRepr(double internalRepr);

    public abstract bool Single();

    public abstract bool Contains(object value);
}

public sealed class DistributionJsonConverter : JsonConverter<Distribution>
{
    public override Distribution Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var name = root.GetProperty("name").GetString()
                   ?? throw new JsonException("Distribution JSON missing 'name'.");
        var attrs = root.GetProperty("attributes");

        switch (name)
        {
            case nameof(FloatDistribution):
            {
                double low = attrs.GetProperty("low").GetDouble();
                double high = attrs.GetProperty("high").GetDouble();
                bool log = attrs.TryGetProperty("log", out var l) && l.GetBoolean();
                double? step = attrs.TryGetProperty("step", out var s) && s.ValueKind != JsonValueKind.Null
                    ? s.GetDouble()
                    : null;
                return new FloatDistribution(low, high, log, step);
            }
            case nameof(IntDistribution):
            {
                long low = attrs.GetProperty("low").GetInt64();
                long high = attrs.GetProperty("high").GetInt64();
                bool log = attrs.TryGetProperty("log", out var l) && l.GetBoolean();
                long step = attrs.TryGetProperty("step", out var s) ? s.GetInt64() : 1;
                return new IntDistribution(low, high, log, step);
            }
            case nameof(CategoricalDistribution):
            {
                var choicesEl = attrs.GetProperty("choices");
                var choices = new object[choicesEl.GetArrayLength()];
                int i = 0;
                foreach (var el in choicesEl.EnumerateArray())
                    choices[i++] = ReadJsonScalar(el);
                return new CategoricalDistribution(choices);
            }
            default:
                throw new JsonException($"Unknown distribution '{name}'.");
        }
    }

    public override void Write(Utf8JsonWriter writer, Distribution value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("name", value.GetType().Name);
        writer.WriteStartObject("attributes");
        switch (value)
        {
            case FloatDistribution f:
                writer.WriteNumber("low", f.Low);
                writer.WriteNumber("high", f.High);
                writer.WriteBoolean("log", f.Log);
                if (f.Step is { } fstep) writer.WriteNumber("step", fstep);
                else writer.WriteNull("step");
                break;
            case IntDistribution n:
                writer.WriteNumber("low", n.Low);
                writer.WriteNumber("high", n.High);
                writer.WriteBoolean("log", n.Log);
                writer.WriteNumber("step", n.Step);
                break;
            case CategoricalDistribution c:
                writer.WritePropertyName("choices");
                writer.WriteStartArray();
                foreach (var choice in c.Choices)
                    WriteJsonScalar(writer, choice);
                writer.WriteEndArray();
                break;
            default:
                throw new JsonException($"Cannot serialize distribution '{value.GetType().Name}'.");
        }
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static object ReadJsonScalar(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString()!,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => el.TryGetInt64(out var i) ? i : el.GetDouble(),
        JsonValueKind.Null => null!,
        _ => throw new JsonException($"Unsupported categorical choice kind '{el.ValueKind}'.")
    };

    private static void WriteJsonScalar(Utf8JsonWriter writer, object value)
    {
        switch (value)
        {
            case null: writer.WriteNullValue(); break;
            case bool b: writer.WriteBooleanValue(b); break;
            case string s: writer.WriteStringValue(s); break;
            case sbyte or byte or short or ushort or int or uint or long:
                writer.WriteNumberValue(Convert.ToInt64(value)); break;
            case float or double or decimal:
                writer.WriteNumberValue(Convert.ToDouble(value)); break;
            default:
                writer.WriteStringValue(value.ToString()); break;
        }
    }
}
