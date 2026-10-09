// SPDX-License-Identifier: GPL-3.0-or-later
#if DEBUG
using System.Text;
using Gradiwin.Core.Annotations;
using Gradiwin.Core.Models;
using Gradiwin.Rendering;
using Gradiwin.Services;
using Microsoft.Graphics.Canvas;

namespace Gradiwin.Diagnostics;

/// <summary>
/// Debug-only end-to-end check of the rendering/export pipeline without UI input:
/// <c>Gradiwin.exe --selftest &lt;image&gt; &lt;outdir&gt;</c>. Writes images and a report to outdir.
/// </summary>
public static class SelfTest
{
    public static async Task<string> RunAsync(string imagePath, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var report = new StringBuilder();
        void Step(string name, Action action)
        {
            try
            {
                action();
                report.AppendLine($"PASS {name}");
            }
            catch (Exception e)
            {
                report.AppendLine($"FAIL {name}: {e.GetType().Name}: {e.Message}");
            }
        }

        using var image = await LoadedImage.LoadAsync(imagePath, ImageOrigin.CommandLine);
        report.AppendLine($"Loaded {image.FullWidth}x{image.FullHeight}, preview {image.PreviewWidth}x{image.PreviewHeight}, balance {image.Balance}");

        var options = new ImageOptions { Padding = 8, CornerRadius = 3, ShadowStrength = 5, AspectRatio = "16:9" };
        var backgrounds = new (string Name, BackgroundSpec Spec)[]
        {
            ("none", BackgroundSpec.None),
            ("solid", new BackgroundSpec { Mode = BackgroundMode.Solid, Solid = new SolidBackground("#3584e4") }),
            ("linear", new BackgroundSpec { Mode = BackgroundMode.Gradient, Gradient = Core.Presets.Palettes.PredefinedGradients[5] }),
            ("radial", new BackgroundSpec { Mode = BackgroundMode.Gradient, Gradient = new Gradient(GradientMode.Radial, Gradient.DefaultSteps(), 0) }),
            ("conic", new BackgroundSpec { Mode = BackgroundMode.Gradient, Gradient = new Gradient(GradientMode.Conic, [new(0, "#f66151"), new(0.5, "#f6d32d"), new(1, "#3584e4")], 60) }),
        };

        foreach (var (name, spec) in backgrounds)
        {
            var o = spec.Mode == BackgroundMode.None ? options.ForNoBackground() : options;
            using var processed = ImageProcessor.Process(image, o, spec, fullResolution: false);
            await processed.SaveAsync(Path.Combine(outDir, $"bg-{name}.png"));
            report.AppendLine($"PASS background {name} → {processed.SizeInPixels.Width}x{processed.SizeInPixels.Height}");
        }

        var rotated = options with { Rotation = 90, AutoBalance = true };
        using (var r = ImageProcessor.Process(image, rotated, backgrounds[2].Spec, false))
            await r.SaveAsync(Path.Combine(outDir, "rotate90-balance.png"));

        // Annotations of every type on the gradient background.
        var spec2 = backgrounds[2].Spec;
        using var preview = ImageProcessor.Process(image, options, spec2, false);
        int pw = (int)preview.SizeInPixels.Width, ph = (int)preview.SizeInPixels.Height;
        var doc = new AnnotationDocument();
        ToolOptions Opt(DrawingMode m, Action<ToolOptions>? tweak = null)
        {
            var t = ToolOptions.CreateDefault(m);
            tweak?.Invoke(t);
            return t;
        }
        double w4 = pw / 4.0, h4 = ph / 4.0;
        doc.Add(new StrokeAnnotation(Enumerable.Range(0, 40).Select(i => new PointD(-w4 * 1.5 + i * 6, -h4 * 1.4 + Math.Sin(i / 4.0) * 20)), Opt(DrawingMode.Pen, o => o.PrimaryColor = new RgbaColor(0.88, 0.11, 0.14))));
        doc.Add(new LineAnnotation(new(-w4 * 1.5, -h4), new(-w4 * 0.5, -h4), false, Opt(DrawingMode.Line, o => o.PrimaryColor = new RgbaColor(0.18, 0.76, 0.49))));
        doc.Add(new ArrowAnnotation(new(-w4 * 1.5, -h4 * 0.5), new(-w4 * 0.4, 0), false, Opt(DrawingMode.Arrow, o => o.PrimaryColor = new RgbaColor(0.88, 0.11, 0.14))));
        doc.Add(new RectAnnotation(new(w4 * 0.2, -h4 * 1.6), new(w4 * 1.4, -h4 * 0.8), false, Opt(DrawingMode.Square, o => { o.PrimaryColor = new RgbaColor(0.88, 0.11, 0.14); o.FillColor = RgbaColor.Transparent; })));
        doc.Add(new EllipseAnnotation(new(w4 * 0.2, -h4 * 0.6), new(w4 * 1.4, h4 * 0.2), false, Opt(DrawingMode.Circle, o => { o.PrimaryColor = RgbaColor.White; o.FillColor = new RgbaColor(0.21, 0.52, 0.89, 0.5); })));
        doc.Add(new HighlighterAnnotation([new(-w4 * 1.2, h4 * 0.5), new(w4 * 0.5, h4 * 0.5)], Opt(DrawingMode.Highlighter), false));
        doc.Add(new CensorAnnotation(new(-w4 * 0.3, -h4 * 0.6), new(w4 * 0.3, h4 * 0.1), Opt(DrawingMode.Censor)) { BlockSizeImage = 8 });
        doc.Add(new TextAnnotation(new(0, h4 * 1.2), "Hello Gradiwin!\nMulti-line ✨", Opt(DrawingMode.Text, o =>
        {
            o.PrimaryColor = RgbaColor.White;
            o.FillColor = new RgbaColor(0.96, 0.38, 0.32);
            o.BorderColor = RgbaColor.Transparent;
        }), 22));
        doc.Add(new TextAnnotation(new(-w4, h4 * 1.6), "Outlined", Opt(DrawingMode.Text, o =>
        {
            o.PrimaryColor = new RgbaColor(0.98, 0.94, 0.42);
            o.FillColor = RgbaColor.Transparent;
            o.BorderColor = RgbaColor.Black;
        }), 28));
        for (int i = 0; i < 3; i++)
            doc.Add(new NumberAnnotation(new(w4 * (0.8 + i * 0.35), h4 * 1.4), doc.NextNumber, Opt(DrawingMode.Number, o => { o.FillColor = new RgbaColor(0.96, 0.38, 0.32); o.PrimaryColor = RgbaColor.White; })));
        report.AppendLine($"Annotations: {doc.Items.Count}, numbers = {string.Join(",", doc.Items.OfType<NumberAnnotation>().Select(n => n.Number))}");

        // Preview-resolution composite (what the canvas shows).
        using (var p = ExportRenderer.RenderPreview(preview, doc.Items, CropRect.Full, includeAnnotations: true))
            await p.SaveAsync(Path.Combine(outDir, "annotated-preview.png"));

        // Full-resolution export in each format.
        using var final = ExportRenderer.Render(image, options, spec2, doc.Items, CropRect.Full, pw, ph);
        report.AppendLine($"Export size {final.SizeInPixels.Width}x{final.SizeInPixels.Height}");
        foreach (var fmt in ImageEncoder.Formats)
        {
            var path = Path.Combine(outDir, "export" + fmt.Extensions[0]);
            await ImageEncoder.SaveAsync(final, path, fmt, compress: true);
            report.AppendLine($"PASS save {fmt.Key}: {new FileInfo(path).Length / 1024} KiB");
        }

        // Crop.
        using (var cropped = ExportRenderer.Render(image, options, spec2, doc.Items, new CropRect(0.25, 0.25, 0.5, 0.5), pw, ph))
        {
            await cropped.SaveAsync(Path.Combine(outDir, "export-cropped.png"));
            report.AppendLine($"PASS crop → {cropped.SizeInPixels.Width}x{cropped.SizeInPixels.Height}");
        }

        // Source snippet.
        Step("source snippet", () =>
        {
            using var snippet = SourceSnippetRenderer.Render(CanvasDevice.GetSharedDevice(), new SnippetOptions(
                SettingsService.DefaultSnippet, SourceSnippetRenderer.FindLanguage("c"), SourceSnippetRenderer.Themes[0], true, "My Code", true, 640));
            snippet.SaveAsync(Path.Combine(outDir, "snippet.png")).AsTask().Wait();
        });

        // OCR on the original image.
        try
        {
            var text = await OcrService.RecognizeAsync(image.Full);
            report.AppendLine($"PASS ocr ({text.Length} chars): {text.Replace('\n', ' ')[..Math.Min(120, text.Length)]}");
        }
        catch (Exception e)
        {
            report.AppendLine($"FAIL ocr: {e.Message}");
        }

        // Localization.
        report.AppendLine($"Locale {Localizer.LanguageTag}: 'Open Image…' → '{Localizer.T("Open Image…")}'");

        var reportPath = Path.Combine(outDir, "report.txt");
        await File.WriteAllTextAsync(reportPath, report.ToString());
        return reportPath;
    }
}
#endif
