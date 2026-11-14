using System.Text.RegularExpressions;

namespace MigrateSQLServerDatabases.Services.Manager;

/// <param name="StartLine">1-based line of the script where the batch starts, to map error lines back to the editor.</param>
/// <param name="Count">"GO 5" runs the batch five times.</param>
public sealed record SqlBatch(string Text, int StartLine, int Count);

/// <summary>T-SQL text helpers that skip comments, strings and quoted identifiers.</summary>
public static partial class SqlText
{
    [GeneratedRegex(@"^\s*GO(?:\s+(\d+))?\s*(?:--.*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex GoLine();

    /// <summary>Splits a script on GO lines, like sqlcmd and SSMS. GO inside comments or strings is ignored.</summary>
    public static List<SqlBatch> SplitBatches(string script)
    {
        var lines = script.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var batches = new List<SqlBatch>();
        var current = new List<string>();
        var startLine = 1;
        var state = new LexState();

        void Flush(int count)
        {
            var text = string.Join("\n", current);
            if (!string.IsNullOrWhiteSpace(text))
                batches.Add(new SqlBatch(text, startLine, Math.Max(count, 1)));
            current.Clear();
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (state.IsNeutral && GoLine().Match(line) is { Success: true } go)
            {
                Flush(go.Groups[1].Success && int.TryParse(go.Groups[1].Value, out var n) ? n : 1);
                startLine = i + 2;
                continue;
            }

            current.Add(line);
            state.Scan(line);
        }

        Flush(1);
        return batches;
    }

    /// <summary>
    /// Turns the first CREATE PROCEDURE/VIEW/FUNCTION/TRIGGER of a script into ALTER
    /// (the script may start with SET options and comments).
    /// </summary>
    public static string ToAlter(string script)
    {
        var words = Words(script).ToList();
        for (var i = 0; i < words.Count; i++)
        {
            if (!words[i].Text.Equals("CREATE", StringComparison.OrdinalIgnoreCase))
                continue;

            // CREATE OR ALTER → ALTER
            var last = i;
            if (i + 2 < words.Count && words[i + 1].Text.Equals("OR", StringComparison.OrdinalIgnoreCase) &&
                words[i + 2].Text.Equals("ALTER", StringComparison.OrdinalIgnoreCase))
                last = i + 2;

            var kind = last + 1 < words.Count ? words[last + 1].Text.ToUpperInvariant() : string.Empty;
            if (kind is not ("PROC" or "PROCEDURE" or "VIEW" or "FUNCTION" or "TRIGGER"))
                continue;

            var end = words[last].Start + words[last].Length;
            return script[..words[i].Start] + "ALTER" + script[end..];
        }

        return script;
    }

    /// <summary>Words outside comments, strings and [quoted] identifiers.</summary>
    public static IEnumerable<(string Text, int Start, int Length)> Words(string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';

            if (c == '-' && next == '-')
            {
                while (i < text.Length && text[i] != '\n') i++;
            }
            else if (c == '/' && next == '*')
            {
                var depth = 1;
                i += 2;
                while (i < text.Length && depth > 0)
                {
                    if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '*') { depth++; i += 2; }
                    else if (text[i] == '*' && i + 1 < text.Length && text[i + 1] == '/') { depth--; i += 2; }
                    else i++;
                }
            }
            else if (c is '\'' or '[' or '"')
            {
                var close = c == '[' ? ']' : c;
                i++;
                while (i < text.Length)
                {
                    if (text[i] == close)
                    {
                        if (i + 1 < text.Length && text[i + 1] == close) { i += 2; continue; }
                        i++;
                        break;
                    }
                    i++;
                }
            }
            else if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '_' or '@' or '#' or '$')) i++;
                yield return (text[start..i], start, i - start);
            }
            else
            {
                i++;
            }
        }
    }

    /// <summary>Tracks multi-line comments, strings and identifiers across lines.</summary>
    private sealed class LexState
    {
        private int commentDepth;
        private char quote;

        public bool IsNeutral => commentDepth == 0 && quote == '\0';

        public void Scan(string line)
        {
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                var next = i + 1 < line.Length ? line[i + 1] : '\0';

                if (commentDepth > 0)
                {
                    if (c == '*' && next == '/') { commentDepth--; i++; }
                    else if (c == '/' && next == '*') { commentDepth++; i++; }
                }
                else if (quote != '\0')
                {
                    if (c == quote)
                    {
                        if (next == quote) i++;
                        else quote = '\0';
                    }
                }
                else if (c == '-' && next == '-')
                {
                    return;
                }
                else if (c == '/' && next == '*')
                {
                    commentDepth++;
                    i++;
                }
                else if (c is '\'' or '"')
                {
                    quote = c;
                }
                else if (c == '[')
                {
                    quote = ']';
                }
            }
        }
    }
}
