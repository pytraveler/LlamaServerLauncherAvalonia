using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace LlamaServerLauncher.Controls;

public static class MarkdownRenderer
{
    private static readonly FontFamily MonospaceFont =
        new("Cascadia Code,Cascadia Mono,Consolas,Menlo,Monospace");

    public static Control Render(string? markdown)
    {
        var root = new StackPanel { Spacing = 6 };
        if (string.IsNullOrEmpty(markdown))
            return root;

        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        int i = 0;
        while (i < lines.Length)
        {
            string line = lines[i];
            string trimmed = line.TrimStart();

            if (trimmed.Length == 0)
            {
                i++;
                continue;
            }

            if (trimmed.StartsWith("```", StringComparison.Ordinal) ||
                trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                string fence = trimmed.Substring(0, 3);
                var code = new StringBuilder();
                i++;
                while (i < lines.Length && !lines[i].TrimStart().StartsWith(fence, StringComparison.Ordinal))
                {
                    code.AppendLine(lines[i]);
                    i++;
                }
                if (i < lines.Length) i++;
                root.Children.Add(BuildCodeBlock(code.ToString().TrimEnd('\n')));
                continue;
            }

            if (IsHorizontalRule(trimmed))
            {
                root.Children.Add(new Border
                {
                    Height = 1,
                    Background = Brush("SeparatorBrush"),
                    Margin = new Thickness(0, 6, 0, 6)
                });
                i++;
                continue;
            }

            int headingLevel = HeadingLevel(trimmed);
            if (headingLevel > 0)
            {
                string text = trimmed.Substring(headingLevel).Trim().TrimEnd('#').Trim();
                root.Children.Add(BuildHeading(text, headingLevel));
                i++;
                continue;
            }

            if (trimmed.StartsWith(">", StringComparison.Ordinal))
            {
                var quote = new StringBuilder();
                while (i < lines.Length && lines[i].TrimStart().StartsWith(">", StringComparison.Ordinal))
                {
                    string q = lines[i].TrimStart();
                    q = q.Substring(1);
                    if (q.StartsWith(" ", StringComparison.Ordinal)) q = q.Substring(1);
                    quote.AppendLine(q);
                    i++;
                }
                root.Children.Add(BuildBlockquote(quote.ToString().TrimEnd('\n')));
                continue;
            }

            if (IsListItem(line, out _, out _, out _))
            {
                var items = new List<(int indent, bool ordered, string marker, string content)>();
                while (i < lines.Length && IsListItem(lines[i], out int indent, out bool ordered, out string content))
                {
                    string marker = ordered ? OrderedMarker(items, indent) : "•";
                    items.Add((indent, ordered, marker, content));
                    i++;
                }
                root.Children.Add(BuildList(items));
                continue;
            }

            var para = new StringBuilder(trimmed);
            i++;
            while (i < lines.Length)
            {
                string next = lines[i];
                string nt = next.TrimStart();
                if (nt.Length == 0 || HeadingLevel(nt) > 0 || IsHorizontalRule(nt) ||
                    nt.StartsWith(">", StringComparison.Ordinal) ||
                    nt.StartsWith("```", StringComparison.Ordinal) ||
                    nt.StartsWith("~~~", StringComparison.Ordinal) ||
                    IsListItem(next, out _, out _, out _))
                    break;
                para.Append(' ').Append(nt);
                i++;
            }
            root.Children.Add(BuildParagraph(para.ToString()));
        }

        return root;
    }

    private static Control BuildHeading(string text, int level)
    {
        double size = level switch
        {
            1 => 24,
            2 => 20,
            3 => 17,
            4 => 15,
            5 => 13,
            _ => 12
        };
        var tb = new SelectableTextBlock
        {
            FontSize = size,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, level <= 2 ? 8 : 4, 0, 2)
        };
        if (level <= 3)
            tb.Foreground = Brush("AccentForegroundBrush");
        ParseInlines(text, tb.Inlines!);
        return tb;
    }

    private static Control BuildParagraph(string text)
    {
        var tb = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap };
        ParseInlines(text, tb.Inlines!);
        return tb;
    }

    private static Control BuildCodeBlock(string code)
    {
        var text = new SelectableTextBlock
        {
            Text = code,
            FontFamily = MonospaceFont,
            Foreground = Brush("CommandForegroundBrush"),
            TextWrapping = TextWrapping.NoWrap
        };
        var scroller = new ScrollViewer
        {
            Content = text,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };
        return new Border
        {
            Background = Brush("CommandBackgroundBrush"),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 8),
            Margin = new Thickness(0, 2, 0, 2),
            Child = scroller
        };
    }

    private static Control BuildBlockquote(string inner)
    {
        var content = Render(inner);
        if (content is StackPanel sp) sp.Spacing = 4;
        return new Border
        {
            BorderBrush = Brush("AccentForegroundBrush"),
            BorderThickness = new Thickness(3, 0, 0, 0),
            Background = Brush("PanelBackgroundBrush"),
            Padding = new Thickness(12, 6),
            Margin = new Thickness(0, 2, 0, 2),
            Child = content
        };
    }

    private static Control BuildList(List<(int indent, bool ordered, string marker, string content)> items)
    {
        var panel = new StackPanel { Spacing = 2 };
        foreach (var (indent, _, marker, content) in items)
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                Margin = new Thickness(12 + indent * 18, 0, 0, 0)
            };
            var bullet = new TextBlock
            {
                Text = marker + " ",
                MinWidth = 18,
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetColumn(bullet, 0);
            var body = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap };
            ParseInlines(content, body.Inlines!);
            Grid.SetColumn(body, 1);
            grid.Children.Add(bullet);
            grid.Children.Add(body);
            panel.Children.Add(grid);
        }
        return panel;
    }

    private static void ParseInlines(string text, InlineCollection target)
    {
        int i = 0;
        var literal = new StringBuilder();

        void Flush()
        {
            if (literal.Length > 0)
            {
                target.Add(new Run(literal.ToString()));
                literal.Clear();
            }
        }

        while (i < text.Length)
        {
            char c = text[i];

            if (c == '`')
            {
                int end = text.IndexOf('`', i + 1);
                if (end > i)
                {
                    Flush();
                    target.Add(new Run(text.Substring(i + 1, end - i - 1))
                    {
                        FontFamily = MonospaceFont,
                        Foreground = Brush("CommandForegroundBrush")
                    });
                    i = end + 1;
                    continue;
                }
            }

            if (c == '[')
            {
                int closeText = text.IndexOf(']', i + 1);
                if (closeText > i && closeText + 1 < text.Length && text[closeText + 1] == '(')
                {
                    int closeUrl = text.IndexOf(')', closeText + 2);
                    if (closeUrl > closeText)
                    {
                        Flush();
                        string label = text.Substring(i + 1, closeText - i - 1);
                        string url = text.Substring(closeText + 2, closeUrl - closeText - 2).Trim();
                        target.Add(BuildLink(label, url));
                        i = closeUrl + 1;
                        continue;
                    }
                }
            }

            if ((c == '*' || c == '_') && i + 1 < text.Length && text[i + 1] == c)
            {
                string delim = new string(c, 2);
                int end = text.IndexOf(delim, i + 2, StringComparison.Ordinal);
                if (end > i)
                {
                    Flush();
                    var bold = new Bold();
                    ParseInlines(text.Substring(i + 2, end - i - 2), bold.Inlines!);
                    target.Add(bold);
                    i = end + 2;
                    continue;
                }
            }

            if (c == '*' || c == '_')
            {
                int end = text.IndexOf(c, i + 1);
                if (end > i && end != i + 1)
                {
                    Flush();
                    var italic = new Italic();
                    ParseInlines(text.Substring(i + 1, end - i - 1), italic.Inlines!);
                    target.Add(italic);
                    i = end + 1;
                    continue;
                }
            }

            literal.Append(c);
            i++;
        }
        Flush();
    }

    private static Inline BuildLink(string label, string url)
    {
        var tb = new TextBlock
        {
            Text = label,
            Foreground = Brush("LinkForegroundBrush"),
            TextDecorations = Avalonia.Media.TextDecorations.Underline,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        ToolTip.SetTip(tb, url);
        tb.PointerPressed += (_, _) => OpenUrl(url);
        return new InlineUIContainer(tb)
        {
            BaselineAlignment = BaselineAlignment.TextBottom
        };
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { }
    }

    private static IBrush? Brush(string key) =>
        Application.Current?.FindResource(key) as IBrush;

    private static int HeadingLevel(string trimmed)
    {
        int n = 0;
        while (n < trimmed.Length && trimmed[n] == '#') n++;
        if (n is >= 1 and <= 6 && n < trimmed.Length && trimmed[n] == ' ')
            return n;
        return 0;
    }

    private static bool IsHorizontalRule(string trimmed)
    {
        string compact = trimmed.Replace(" ", "");
        if (compact.Length < 3) return false;
        char c = compact[0];
        if (c != '-' && c != '*' && c != '_') return false;
        foreach (char ch in compact)
            if (ch != c) return false;
        return true;
    }

    private static bool IsListItem(string line, out int indent, out bool ordered, out string content)
    {
        indent = 0;
        ordered = false;
        content = string.Empty;

        int spaces = 0;
        while (spaces < line.Length && (line[spaces] == ' ' || line[spaces] == '\t')) spaces++;
        indent = spaces / 2;
        string rest = line.Substring(spaces);
        if (rest.Length < 2) return false;

        if ((rest[0] == '-' || rest[0] == '*' || rest[0] == '+') && rest[1] == ' ')
        {
            content = rest.Substring(2).Trim();
            return true;
        }

        int d = 0;
        while (d < rest.Length && char.IsDigit(rest[d])) d++;
        if (d > 0 && d + 1 < rest.Length && (rest[d] == '.' || rest[d] == ')') && rest[d + 1] == ' ')
        {
            ordered = true;
            content = rest.Substring(d + 2).Trim();
            return true;
        }
        return false;
    }

    private static string OrderedMarker(
        List<(int indent, bool ordered, string marker, string content)> existing, int indent)
    {
        int count = 1;
        for (int k = existing.Count - 1; k >= 0; k--)
        {
            if (existing[k].indent < indent) break;
            if (existing[k].indent == indent)
            {
                if (!existing[k].ordered) break;
                count++;
            }
        }
        return count + ".";
    }
}
