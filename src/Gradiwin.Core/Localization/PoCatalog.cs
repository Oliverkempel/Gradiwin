// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;

namespace Gradiwin.Core.Localization;

/// <summary>
/// Minimal gettext .po reader so Gradia's translations can be reused unchanged.
/// Lookup key is the English msgid (optionally "context\u0004msgid").
/// </summary>
public sealed class PoCatalog
{
    private readonly Dictionary<string, string> _entries = new(StringComparer.Ordinal);

    public int Count => _entries.Count;

    public string? Lookup(string msgid, string? context = null)
    {
        if (context != null && _entries.TryGetValue(context + "\u0004" + msgid, out var ctxValue))
            return ctxValue;
        return _entries.TryGetValue(msgid, out var v) ? v : null;
    }

    public static PoCatalog Parse(string content)
    {
        var catalog = new PoCatalog();
        string? ctx = null, id = null, str = null;
        bool fuzzy = false;
        StringBuilder? current = null;
        string? currentField = null;

        void Commit()
        {
            if (current != null && currentField != null)
            {
                var value = current.ToString();
                switch (currentField)
                {
                    case "msgctxt": ctx = value; break;
                    case "msgid": id = value; break;
                    case "msgstr": str = value; break;
                }
            }
            current = null;
            currentField = null;
        }

        void Flush()
        {
            Commit();
            if (!fuzzy && !string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(str))
                catalog._entries[ctx != null ? ctx + "\u0004" + id : id] = str;
            ctx = id = str = null;
            fuzzy = false;
        }

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0)
            {
                Flush();
                continue;
            }
            if (line.StartsWith("#,") && line.Contains("fuzzy"))
            {
                fuzzy = true;
                continue;
            }
            if (line.StartsWith('#'))
                continue;

            if (line.StartsWith('"'))
            {
                current?.Append(Unquote(line));
                continue;
            }

            int space = line.IndexOf(' ');
            if (space < 0) continue;
            var keyword = line[..space];
            var rest = line[(space + 1)..].Trim();

            if (keyword is "msgctxt" or "msgid" && (str != null || currentField == "msgstr"))
                Flush();

            Commit();
            switch (keyword)
            {
                case "msgctxt":
                case "msgid":
                case "msgstr":
                    currentField = keyword;
                    current = new StringBuilder(Unquote(rest));
                    break;
                case "msgstr[0]":
                    currentField = "msgstr";
                    current = new StringBuilder(Unquote(rest));
                    break;
                default:
                    // msgid_plural / msgstr[n>0]: ignored, singular form is used.
                    break;
            }
        }
        Flush();
        return catalog;
    }

    private static string Unquote(string s)
    {
        s = s.Trim();
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
            s = s[1..^1];
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\\' && i + 1 < s.Length)
            {
                char n = s[++i];
                sb.Append(n switch
                {
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    '"' => '"',
                    '\\' => '\\',
                    _ => n,
                });
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
