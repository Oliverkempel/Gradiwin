// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using Gradiwin.Core.Localization;
using Microsoft.UI.Xaml.Markup;

namespace Gradiwin.Services;

/// <summary>
/// gettext-style translation using Gradia's .po files (shipped in Assets/Locales).
/// Strings are looked up by their English text, so Gradia's translations apply unchanged.
/// </summary>
public static class Localizer
{
    private static PoCatalog? s_catalog;
    private static bool s_loaded;

    public static string LanguageTag { get; private set; } = "en";

    private static void EnsureLoaded()
    {
        if (s_loaded) return;
        s_loaded = true;
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "Assets", "Locales");
            foreach (var culture in CandidateCultures())
            {
                var file = Path.Combine(dir, culture + ".po");
                if (File.Exists(file))
                {
                    s_catalog = PoCatalog.Parse(File.ReadAllText(file));
                    LanguageTag = culture;
                    return;
                }
            }
        }
        catch (IOException)
        {
            s_catalog = null;
        }
    }

    private static IEnumerable<string> CandidateCultures()
    {
        var tags = new List<string>();
        try
        {
            tags.AddRange(Windows.System.UserProfile.GlobalizationPreferences.Languages);
        }
        catch (Exception)
        {
            // Not available in every context.
        }
        tags.Add(CultureInfo.CurrentUICulture.Name);

        foreach (var tag in tags)
        {
            if (string.IsNullOrEmpty(tag)) continue;
            var parts = tag.Split('-');
            var lang = parts[0].ToLowerInvariant();
            if (lang == "en") yield break;
            if (parts.Length > 1)
            {
                // zh-Hans-CN → zh_CN, pt-BR → pt_BR, zh-Hant-TW → zh_TW
                var region = parts[^1].ToUpperInvariant();
                if (lang == "zh" && tag.Contains("Hans", StringComparison.OrdinalIgnoreCase)) region = "CN";
                if (lang == "zh" && tag.Contains("Hant", StringComparison.OrdinalIgnoreCase)) region = "TW";
                yield return $"{lang}_{region}";
            }
            yield return lang;
        }
    }

    /// <summary>Translate an English msgid.</summary>
    public static string T(string msgid)
    {
        EnsureLoaded();
        return s_catalog?.Lookup(msgid) ?? msgid;
    }

    public static string T(string msgid, string context)
    {
        EnsureLoaded();
        return s_catalog?.Lookup(msgid, context) ?? msgid;
    }

    /// <summary>Translate then format with invariant placeholders ({0}, …).</summary>
    public static string F(string msgid, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(msgid), args);

    /// <summary>
    /// Gradia uses python "{name}" style placeholders in some strings; this replaces them.
    /// </summary>
    public static string Named(string msgid, params (string Name, object Value)[] values)
    {
        var s = T(msgid);
        foreach (var (name, value) in values)
            s = s.Replace("{" + name + "}", Convert.ToString(value, CultureInfo.CurrentCulture), StringComparison.Ordinal);
        return s;
    }

    /// <summary>Removes the GTK mnemonic underscore ("_Open" → "Open").</summary>
    public static string StripMnemonic(string s) => s.Replace("_", "", StringComparison.Ordinal);
}

/// <summary>XAML markup extension: <c>Text="{l:Tr Text='Open Image…'}"</c>.</summary>
[MarkupExtensionReturnType(ReturnType = typeof(string))]
public sealed partial class Tr : MarkupExtension
{
    public string Text { get; set; } = "";

    public bool StripMnemonic { get; set; } = true;

    protected override object ProvideValue()
    {
        var s = Localizer.T(Text);
        return StripMnemonic ? Localizer.StripMnemonic(s) : s;
    }
}
