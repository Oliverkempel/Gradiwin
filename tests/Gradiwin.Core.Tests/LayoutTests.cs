using Gradiwin.Core.Algorithms;
using Gradiwin.Core.Models;

namespace Gradiwin.Core.Tests;

public class LayoutTests
{
    [Fact]
    public void PaddingIsPercentOfSmallerSide()
    {
        var l = Layout.Compute(200, 100, new ImageOptions { Padding = 10, AspectRatio = "" }, null);
        Assert.Equal((220, 120), (l.FinalWidth, l.FinalHeight));
        Assert.Equal((10, 10), (l.PasteX, l.PasteY));
    }

    [Theory]
    [InlineData("16:9", 220, 120, 220, 123)]
    [InlineData("16:9", 100, 120, 213, 120)]
    [InlineData("1:1", 220, 120, 220, 220)]
    [InlineData("", 220, 120, 220, 120)]
    public void AspectRatioExpandsOneDimension(string ratio, int w, int h, int ew, int eh)
    {
        var (fw, fh) = Layout.FinalDimensions(w, h, 0, ratio);
        Assert.Equal((ew, eh), (fw, fh));
    }

    [Fact]
    public void RotationSwapsDimensions()
    {
        var l = Layout.Compute(200, 100, new ImageOptions { Padding = 0, Rotation = 90 }, null);
        Assert.Equal((100, 200), (l.ContentWidth, l.ContentHeight));
    }

    [Fact]
    public void NegativePaddingCropsInward()
    {
        var l = Layout.Compute(200, 100, new ImageOptions { Padding = -10 }, null);
        Assert.Equal((180, 80), (l.ContentWidth, l.ContentHeight));
        Assert.Equal((10, 10, 180, 80), l.NegativeCrop);
        Assert.Equal((0, 0), (l.PasteX, l.PasteY));
    }

    [Fact]
    public void ShadowParametersMatchGradia()
    {
        var l = Layout.Compute(100, 100, new ImageOptions { ShadowStrength = 5 }, null);
        Assert.Equal(10, l.ShadowBlurRadius);
        Assert.Equal(1.0, l.ShadowAlpha);
        var full = Layout.Compute(100, 100, new ImageOptions { ShadowStrength = 5 }, null, scale: 2.5);
        Assert.Equal(25, full.ShadowBlurRadius);
    }

    [Fact]
    public void PreviewSizeCapsAtOneMegapixel()
    {
        var (w, h) = Layout.PreviewSize(4000, 3000);
        Assert.True(w * h <= Layout.MaxPreviewPixels);
        Assert.Equal(1182, w);
        Assert.Equal(886, h);
    }

    [Theory]
    [InlineData("16:9", 16.0 / 9)]
    [InlineData("1.5", 1.5)]
    [InlineData("", null)]
    [InlineData("3:0", null)]
    [InlineData("abc", null)]
    public void ParsesAspectRatios(string text, double? expected)
    {
        var r = Layout.ParseAspectRatio(text);
        if (expected is null) Assert.Null(r);
        else Assert.Equal(expected.Value, r!.Value, 6);
    }

    [Fact]
    public void PaddingAnalyzerBalancesBorders()
    {
        // 10x6 white image with a black pixel block at x=2..3, y=1..2 → top=1, bottom=3, left=2, right=6.
        const int w = 10, h = 6;
        var px = new byte[w * h * 4];
        Array.Fill(px, (byte)255);
        foreach (var (x, y) in new[] { (2, 1), (3, 1), (2, 2), (3, 2) })
        {
            int i = (y * w + x) * 4;
            px[i] = px[i + 1] = px[i + 2] = 0;
        }
        var bp = PaddingAnalyzer.Analyze(px, w, h);
        Assert.Equal((5, 3, 4, 0), (bp.Top, bp.Bottom, bp.Left, bp.Right));
        Assert.Equal(RgbaColor.White, bp.Color);
    }
}
