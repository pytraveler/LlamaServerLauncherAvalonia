using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LlamaServerLauncher.Models.Optimization;

namespace LlamaServerLauncher.Services.Optimization;

public static class BenchCsvParser
{
    public static BenchmarkResult Parse(string csv)
    {
        var rows = ParseRows(csv);
        if (rows.Count < 2)
            return new BenchmarkResult { RawCsv = csv };

        var header = rows[0];
        int colNGen = IndexOf(header, "n_gen");
        int colNPrompt = IndexOf(header, "n_prompt");
        int colAvg = IndexOf(header, "avg_ts");
        int colStd = IndexOf(header, "stddev_ts");
        int colNgl = IndexOf(header, "n_gpu_layers");

        double tg = 0, tgStd = 0, pp = 0, ppStd = 0;
        bool tgFound = false, ppFound = false;
        int? gpuLayers = null;

        for (int r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            double nGen = GetDouble(row, colNGen);
            double nPrompt = GetDouble(row, colNPrompt);

            if (gpuLayers is null && colNgl >= 0 && colNgl < row.Count
                && int.TryParse(row[colNgl].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ngl))
                gpuLayers = ngl;

            if (!tgFound && nGen > 0)
            {
                tg = GetDouble(row, colAvg);
                tgStd = GetDouble(row, colStd);
                tgFound = true;
            }
            if (!ppFound && nPrompt > 0)
            {
                pp = GetDouble(row, colAvg);
                ppStd = GetDouble(row, colStd);
                ppFound = true;
            }
        }

        return new BenchmarkResult { TgTs = tg, TgStddev = tgStd, PpTs = pp, PpStddev = ppStd, GpuLayers = gpuLayers, RawCsv = csv };
    }

    private static List<List<string>> ParseRows(string csv)
    {
        var rows = new List<List<string>>();
        foreach (var rawLine in csv.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0)
                continue;
            rows.Add(ParseLine(line));
        }
        return rows;
    }

    private static List<string> ParseLine(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(ch);
            }
            else
            {
                if (ch == '"') inQuotes = true;
                else if (ch == ',') { fields.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(ch);
            }
        }
        fields.Add(sb.ToString());
        return fields;
    }

    private static int IndexOf(List<string> header, string column)
    {
        for (int i = 0; i < header.Count; i++)
            if (header[i].Trim().Equals(column, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    private static double GetDouble(List<string> row, int col)
    {
        if (col < 0 || col >= row.Count)
            return 0;
        return double.TryParse(row[col].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }
}
