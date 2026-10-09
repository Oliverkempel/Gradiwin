// SPDX-License-Identifier: GPL-3.0-or-later
using System.Numerics;
using ColorCode;
using ColorCode.Common;
using ColorCode.Parsing;
using ColorCode.Styling;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Windows.UI;

namespace Gradiwin.Rendering;

public sealed record SnippetTheme(string Name, StyleDictionary Styles, Color Background, Color Foreground, Color Gutter, Color TitleBar, Color TitleText);

public sealed record SnippetOptions(string Code, ILanguage? Language, SnippetTheme Theme, bool ShowFrame, string Title, bool ShowLineNumbers, float Width);

/// <summary>
/// Renders a syntax-highlighted code image (replaces Gradia's GtkSourceView snapshot in
/// ui/image_creation/source_image_generator.py: 2× scale, 24 px padding, 12 px rounded corners).
/// </summary>
public static class SourceSnippetRenderer
{
    public const float Scale = 2f;
    public const float OuterPadding = 24f;
    public const float CornerRadius = 12f;
    private const float FontSize = 14f;
    private const float InnerPadding = 18f;
    private const float TitleBarHeight = 38f;

    public static readonly IReadOnlyList<SnippetTheme> Themes =
    [
        new("Dark", StyleDictionary.DefaultDark, Color.FromArgb(255, 0x1e, 0x1e, 0x1e), Color.FromArgb(255, 0xd4, 0xd4, 0xd4),
            Color.FromArgb(255, 0x85, 0x85, 0x85), Color.FromArgb(255, 0x2d, 0x2d, 0x2d), Color.FromArgb(255, 0xcc, 0xcc, 0xcc)),
        new("Light", StyleDictionary.DefaultLight, Color.FromArgb(255, 0xff, 0xff, 0xff), Color.FromArgb(255, 0x24, 0x29, 0x2e),
            Color.FromArgb(255, 0x9a, 0x9a, 0x9a), Color.FromArgb(255, 0xeb, 0xeb, 0xeb), Color.FromArgb(255, 0x3d, 0x3d, 0x3d)),
    ];

    private static string? s_monoFont;

    /// <summary>First installed monospace font (Win2D takes a single family name, not a CSS-style list).</summary>
    public static string MonospaceFont => s_monoFont ??= new[] { "Cascadia Mono", "Cascadia Code", "Consolas", "Courier New" }
        .FirstOrDefault(f => CanvasTextFormat.GetSystemFontFamilies().Contains(f)) ?? "Consolas";

    public static IReadOnlyList<ILanguage> Languages => ColorCode.Languages.All.OrderBy(l => l.Name).ToList();

    public static ILanguage? FindLanguage(string id) =>
        ColorCode.Languages.FindById(id) ?? (id == "c" ? ColorCode.Languages.FindById("cpp") : null);

    /// <summary>Collects (start, length, scope) tokens using ColorCode's parser.</summary>
    private sealed class TokenCollector : CodeColorizerBase
    {
        private int _offset;

        public TokenCollector(StyleDictionary styles) : base(styles, null) { }

        public List<(int Start, int Length, string Scope)> Tokens { get; } = [];

        public void Run(string code, ILanguage language)
        {
            _offset = 0;
            languageParser.Parse(code, language, (parsed, scopes) => Write(parsed, scopes));
        }

        protected override void Write(string parsedSourceCode, IList<Scope> scopes)
        {
            void Add(Scope s)
            {
                Tokens.Add((_offset + s.Index, s.Length, s.Name));
                foreach (var c in s.Children) Add(c);
            }
            foreach (var s in scopes) Add(s);
            _offset += parsedSourceCode.Length;
        }
    }

    private static Color? ParseColor(string? hex)
    {
        if (string.IsNullOrEmpty(hex)) return null;
        var h = hex.TrimStart('#');
        if (!uint.TryParse(h, System.Globalization.NumberStyles.HexNumber, null, out var v)) return null;
        return h.Length switch
        {
            8 => Color.FromArgb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v),
            6 => Color.FromArgb(255, (byte)(v >> 16), (byte)(v >> 8), (byte)v),
            _ => null,
        };
    }

    public static CanvasRenderTarget Render(ICanvasResourceCreator device, SnippetOptions o)
    {
        var code = o.Code.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Replace("\t", "    ", StringComparison.Ordinal);
        if (code.Length == 0) code = " ";
        var lines = code.Split('\n');

        using var format = new CanvasTextFormat
        {
            FontFamily = MonospaceFont,
            FontSize = FontSize,
            WordWrapping = CanvasWordWrapping.Wrap,
        };

        float gutterWidth = 0;
        if (o.ShowLineNumbers)
        {
            using var probe = new CanvasTextLayout(device, new string('9', lines.Length.ToString(System.Globalization.CultureInfo.InvariantCulture).Length), format, 1000, 100);
            gutterWidth = (float)probe.LayoutBounds.Width + 16;
        }

        float cardWidth = Math.Max(200, o.Width);
        float textWidth = cardWidth - 2 * InnerPadding - gutterWidth;
        using var layout = new CanvasTextLayout(device, code, format, textWidth, 100000);
        layout.SetColor(0, code.Length, o.Theme.Foreground);
        if (o.Language != null)
        {
            var collector = new TokenCollector(o.Theme.Styles);
            collector.Run(code, o.Language);
            foreach (var (start, length, scope) in collector.Tokens)
            {
                if (start < 0 || start + length > code.Length) continue;
                if (o.Theme.Styles.Contains(scope) && ParseColor(o.Theme.Styles[scope].Foreground) is { } c)
                    layout.SetColor(start, length, c);
            }
        }

        float textHeight = (float)layout.LayoutBounds.Height;
        float top = o.ShowFrame ? TitleBarHeight : 0;
        float cardHeight = top + InnerPadding * 2 + textHeight;

        float w = (cardWidth + 2 * OuterPadding) * Scale, h = (cardHeight + 2 * OuterPadding) * Scale;
        var target = new CanvasRenderTarget(device, w, h, 96);
        using var ds = target.CreateDrawingSession();
        ds.Clear(Microsoft.UI.Colors.Transparent);
        ds.Transform = Matrix3x2.CreateScale(Scale) * Matrix3x2.CreateTranslation(OuterPadding * Scale, OuterPadding * Scale);

        using var clip = CanvasGeometry.CreateRoundedRectangle(ds, 0, 0, cardWidth, cardHeight, CornerRadius, CornerRadius);
        using (ds.CreateLayer(1f, clip))
        {
            ds.FillRectangle(0, 0, cardWidth, cardHeight, o.Theme.Background);
            if (o.ShowFrame)
            {
                ds.FillRectangle(0, 0, cardWidth, TitleBarHeight, o.Theme.TitleBar);
                var dots = new[] { Color.FromArgb(255, 0xff, 0x5f, 0x57), Color.FromArgb(255, 0xfe, 0xbc, 0x2e), Color.FromArgb(255, 0x28, 0xc8, 0x40) };
                for (int i = 0; i < 3; i++)
                    ds.FillCircle(new Vector2(18 + i * 20, TitleBarHeight / 2), 6, dots[i]);
                using var titleFormat = new CanvasTextFormat
                {
                    FontFamily = "Segoe UI Variable Text",
                    FontSize = 13,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    HorizontalAlignment = CanvasHorizontalAlignment.Center,
                    VerticalAlignment = CanvasVerticalAlignment.Center,
                    WordWrapping = CanvasWordWrapping.NoWrap,
                    TrimmingGranularity = CanvasTextTrimmingGranularity.Character,
                };
                ds.DrawText(o.Title, new Windows.Foundation.Rect(80, 0, cardWidth - 160, TitleBarHeight), o.Theme.TitleText, titleFormat);
            }

            float textX = InnerPadding + gutterWidth, textY = top + InnerPadding;
            if (o.ShowLineNumbers)
            {
                // Number each logical line at the position of its first visual line.
                using var numberFormat = new CanvasTextFormat
                {
                    FontFamily = format.FontFamily,
                    FontSize = FontSize,
                    HorizontalAlignment = CanvasHorizontalAlignment.Right,
                };
                int charIndex = 0;
                for (int i = 0; i < lines.Length; i++)
                {
                    var caret = layout.GetCaretPosition(charIndex, false);
                    ds.DrawText((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                        new Windows.Foundation.Rect(InnerPadding - 4, textY + caret.Y, gutterWidth - 12, FontSize * 2),
                        o.Theme.Gutter, numberFormat);
                    charIndex += lines[i].Length + 1;
                }
            }
            ds.DrawTextLayout(layout, textX, textY, o.Theme.Foreground);
        }
        return target;
    }
}
