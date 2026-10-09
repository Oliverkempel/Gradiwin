using Gradiwin.Core.Algorithms;
using Gradiwin.Core.Localization;
using Gradiwin.Core.Models;

namespace Gradiwin.Core.Tests;

public class MiscTests
{
    [Theory]
    [InlineData("#ff3584e4", 0x35, 0x84, 0xe4, 255)]
    [InlineData("#00000000", 0, 0, 0, 0)]
    [InlineData("#4A90E2", 0x4a, 0x90, 0xe2, 255)]
    [InlineData("rgb(87,227,137)", 87, 227, 137, 255)]
    public void ParsesColors(string s, int r, int g, int b, int a)
    {
        var c = RgbaColor.Parse(s);
        Assert.Equal((r, g, b, a), ((int)c.R8, (int)c.G8, (int)c.B8, (int)c.A8));
    }

    [Fact]
    public void ParsesPoFiles()
    {
        const string po = """
            msgid ""
            msgstr ""
            "Content-Type: text/plain; charset=UTF-8\n"

            #: gradia/ui/window.py:1
            msgid "Open Image…"
            msgstr "Bild öffnen…"

            #, fuzzy
            msgid "Fuzzy"
            msgstr "Unscharf"

            msgid "Multi"
            "line"
            msgstr "Mehr"
            "zeilig\n"

            msgid "Untranslated"
            msgstr ""
            """;
        var cat = PoCatalog.Parse(po);
        Assert.Equal("Bild öffnen…", cat.Lookup("Open Image…"));
        Assert.Null(cat.Lookup("Fuzzy"));
        Assert.Equal("Mehrzeilig\n", cat.Lookup("Multiline"));
        Assert.Null(cat.Lookup("Untranslated"));
    }

    [Fact]
    public void ParsesRealGradiaTranslationsIfAvailable()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Gradiwin", "Assets", "Locales", "de.po"));
        if (!File.Exists(path)) return;
        var cat = PoCatalog.Parse(File.ReadAllText(path));
        Assert.True(cat.Count > 50);
    }

    [Fact]
    public void ParsesCommandLine()
    {
        var o = CommandLineOptions.Parse(["--screenshot=FULL", "--delay=1500", "a.png", "--pin", "b.png", "c.png"]);
        Assert.Equal(ScreenshotMode.Full, o.Screenshot);
        Assert.Equal(1500, o.DelayMs);
        Assert.Equal(["a.png"], o.Files);
        Assert.Equal(["b.png", "c.png"], o.PinFiles);

        var d = CommandLineOptions.Parse(["--screenshot"]);
        Assert.Equal(ScreenshotMode.Interactive, d.Screenshot);

        var f = CommandLineOptions.Parse(["--ocr-file", "x.png"]);
        Assert.Equal("x.png", f.OcrFile);
    }

    [Fact]
    public void SplitsWindowsCommandLines()
    {
        var args = CommandLineOptions.SplitCommandLine("gradia.exe \"C:\\My Pictures\\a.png\" --delay=10");
        Assert.Equal(["gradia.exe", "C:\\My Pictures\\a.png", "--delay=10"], args);
    }

    [Fact]
    public void CropClampsAndKeepsAspect()
    {
        var crop = new CropModel();
        crop.Set(CropRect.Initial);
        crop.SetAspectRatio(1.0, 200, 100);
        // relative aspect = 1*100/200 = 0.5 → width = height*0.5
        Assert.Equal(crop.Height * 0.5, crop.Width, 6);

        crop.BeginDrag(1000, 1000, 0, 0, 200, 100);
        Assert.False(crop.IsDragging);

        var free = new CropModel();
        free.Set(new CropRect(0, 0, 1, 1));
        Assert.True(free.BeginDrag(100, 50, 0, 0, 200, 100)); // area drag
        free.EndDrag();
        Assert.True(free.BeginDrag(200, 100, 0, 0, 200, 100)); // bottom-right corner
        free.UpdateDrag(0, 0, 200, 100);
        Assert.Equal(CropModel.MinDimension, free.Width, 6);
        Assert.Equal(CropModel.MinDimension, free.Height, 6);
    }
}
