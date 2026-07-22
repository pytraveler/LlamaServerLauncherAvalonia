using System;
using System.Collections.Generic;
using System.Text;

namespace LlamaServerLauncher.Models;

public static class CommandLineParser
{
    public static bool IsFlag(string token)
    {
        if (string.IsNullOrEmpty(token) || token[0] != '-')
            return false;

        if (token.Length < 2)
            return true;

        char next = token[1];
        if (char.IsDigit(next) || next == '.')
            return false;

        return true;
    }

    public static string NormalizeSpecialCharacters(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var result = input
            .Replace("\\t", "\t")
            .Replace("\\n", "\n")
            .Replace("\\r", "\r");

        result = result.Replace("\\\\", "\\");

        result = result.Replace("\t", " ").Replace("\n", " ").Replace("\r", " ");

        return result;
    }

    public static List<string> ParseArguments(string args)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(args))
            return result;

        var sb = new StringBuilder();
        bool inQuotes = false;
        char? quoteChar = null;

        for (int i = 0; i < args.Length; i++)
        {
            char c = args[i];
            
            if (inQuotes && c == '\\' && i + 1 < args.Length)
            {
                char nextC = args[i + 1];
                if (nextC == '"' || nextC == '\\')
                {
                    sb.Append(c);
                    sb.Append(nextC);
                    i++;
                    continue;
                }
            }

            if (!inQuotes && (c == '"' || c == '\''))
            {
                inQuotes = true;
                quoteChar = c;
            }
            else if (inQuotes && c == quoteChar)
            {
                inQuotes = false;
                quoteChar = null;
            }
            else if (!inQuotes && (c == ' ' || c == '\t'))
            {
                if (sb.Length > 0)
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                }
            }
            else
            {
                sb.Append(c);
            }
        }

        if (sb.Length > 0)
            result.Add(sb.ToString());

        return result;
    }

    public static Dictionary<string, string?> GetArgumentValues(List<string> args)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];

            if (IsFlag(arg))
            {
                if (i + 1 < args.Count && !IsFlag(args[i + 1]))
                {
                    result[arg] = args[i + 1];
                    i++;
                }
                else
                {
                    result[arg] = null;
                }
            }
        }

        return result;
    }

    public static HashSet<string> GetArgumentFlags(List<string> args)
    {
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var arg in args)
        {
            if (IsFlag(arg))
            {
                flags.Add(arg);
            }
        }
        
        return flags;
    }
}