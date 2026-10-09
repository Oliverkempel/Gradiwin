// Usage: IconRenderer <app-icon.svg> <output Assets dir>
using System.Drawing;
using System.Drawing.Imaging;
using Svg;

var svg = SvgDocument.Open(args[0]);
var outDir = args[1];
Directory.CreateDirectory(outDir);

void Square(string name, int size, double inset = 0)
{
    using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(bmp))
    {
        g.Clear(Color.Transparent);
        int pad = (int)(size * inset);
        using var icon = svg.Draw(size - 2 * pad, size - 2 * pad);
        g.DrawImage(icon, pad, pad);
    }
    bmp.Save(Path.Combine(outDir, name), ImageFormat.Png);
}

void Wide(string name, int w, int h)
{
    using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(bmp))
    {
        g.Clear(Color.Transparent);
        int s = (int)(h * 0.8);
        using var icon = svg.Draw(s, s);
        g.DrawImage(icon, (w - s) / 2, (h - s) / 2);
    }
    bmp.Save(Path.Combine(outDir, name), ImageFormat.Png);
}

foreach (var scale in new[] { 100, 125, 150, 200, 400 })
{
    double f = scale / 100.0;
    Square($"Square44x44Logo.scale-{scale}.png", (int)(44 * f));
    Square($"Square150x150Logo.scale-{scale}.png", (int)(150 * f), 0.15);
    Square($"StoreLogo.scale-{scale}.png", (int)(50 * f));
    Wide($"Wide310x150Logo.scale-{scale}.png", (int)(310 * f), (int)(150 * f));
    Wide($"SplashScreen.scale-{scale}.png", (int)(620 * f), (int)(300 * f));
}
foreach (var size in new[] { 16, 24, 32, 48, 256 })
{
    Square($"Square44x44Logo.targetsize-{size}.png", size);
    Square($"Square44x44Logo.targetsize-{size}_altform-unplated.png", size);
    Square($"Square44x44Logo.targetsize-{size}_altform-lightunplated.png", size);
}
Square("AppIcon256.png", 256);

// Multi-size .ico for the window/taskbar icon.
using (var fs = File.Create(Path.Combine(outDir, "AppIcon.ico")))
using (var bw = new BinaryWriter(fs))
{
    int[] sizes = [16, 24, 32, 48, 64, 256];
    var pngs = sizes.Select(s =>
    {
        using var bmp = svg.Draw(s, s);
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }).ToArray();
    bw.Write((short)0); bw.Write((short)1); bw.Write((short)sizes.Length);
    int offset = 6 + 16 * sizes.Length;
    for (int i = 0; i < sizes.Length; i++)
    {
        bw.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
        bw.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
        bw.Write((byte)0); bw.Write((byte)0);
        bw.Write((short)1); bw.Write((short)32);
        bw.Write(pngs[i].Length); bw.Write(offset);
        offset += pngs[i].Length;
    }
    foreach (var p in pngs) bw.Write(p);
}
Console.WriteLine("done");
