using Gradiwin.Core.Algorithms;
using Gradiwin.Core.Models;

namespace Gradiwin.Core.Tests;

public class GradientGeneratorTests
{
    // Reference values computed with a line-by-line transcription of Gradia's gradient_gen.c
    // (including the conic stop remapping from gradient.py) on a 32x24 canvas.
    public static TheoryData<string, int, int, int, int, int> Cases => new()
    {
        { "lin135", 0, 0, 67, 172, 189 },
        { "lin135", 13, 7, 71, 183, 179 },
        { "lin135", 5, 20, 58, 146, 214 },
        { "lin45three", 0, 0, 35, 212, 253 },
        { "lin45three", 31, 23, 183, 33, 255 },
        { "lin45three", 20, 12, 81, 129, 242 },
        { "radial", 0, 0, 53, 132, 228 },
        { "radial", 13, 7, 77, 199, 163 },
        { "radial", 20, 12, 80, 208, 155 },
        { "conic30", 0, 0, 76, 197, 165 },
        { "conic30", 13, 7, 85, 223, 140 },
        { "conic30", 31, 23, 68, 175, 186 },
        { "conic30", 5, 20, 56, 142, 217 },
    };

    private static Gradient Make(string name) => name switch
    {
        "lin135" => new Gradient(GradientMode.Linear, Gradient.DefaultSteps(), 135),
        "lin45three" => new Gradient(GradientMode.Linear,
            [new(0.0, "#23d4fd"), new(0.5, "#3a98f0"), new(1.0, "#b721ff")], 45),
        "radial" => new Gradient(GradientMode.Radial, Gradient.DefaultSteps(), 0),
        "conic30" => new Gradient(GradientMode.Conic, Gradient.DefaultSteps(), 30),
        _ => throw new ArgumentException(name),
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void MatchesGradiaReference(string name, int x, int y, int r, int g, int b)
    {
        const int w = 32, h = 24;
        var px = GradientGenerator.GenerateBgra(Make(name), w, h);
        int i = (y * w + x) * 4;
        Assert.Equal((b, g, r, 255), (px[i], px[i + 1], px[i + 2], px[i + 3]));
    }

    [Fact]
    public void ConicStopsAreRotatedAndWrapped()
    {
        var stops = GradientGenerator.PrepareStops(new Gradient(GradientMode.Conic, Gradient.DefaultSteps(), 0));
        Assert.Equal(3, stops.Count);
        Assert.Equal(0.05, stops[0].Position, 6);
        Assert.Equal(0.95, stops[1].Position, 6);
        Assert.Equal(1.0, stops[2].Position);
        Assert.Equal(stops[0].Color, stops[2].Color);
    }

    [Fact]
    public void JsonRoundTripIsGradiaCompatible()
    {
        const string gradia = "{\"mode\": \"conic\", \"steps\": [[0.0, \"rgb(87,227,137)\"], [1.0, \"rgb(53,132,228)\"]], \"angle\": 135}";
        var g = Gradient.FromJson(gradia);
        Assert.Equal(GradientMode.Conic, g.Mode);
        Assert.Equal(135, g.Angle);
        Assert.Equal(2, g.Steps.Count);
        var again = Gradient.FromJson(g.ToJson());
        Assert.True(g.ContentEquals(again));
    }

    [Fact]
    public void InvalidJsonFallsBackToDefault()
    {
        var g = Gradient.FromJson("not json");
        Assert.Equal(GradientMode.Linear, g.Mode);
        Assert.Equal(135, g.Angle);
    }
}
