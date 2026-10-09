// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;

namespace Gradiwin.Core;

public enum ScreenshotMode
{
    None,
    Interactive,
    Full,
}

/// <summary>Parsed command line (port of gradia.in / main.py argument handling).</summary>
public sealed record CommandLineOptions
{
    public bool ShowHelp { get; init; }
    public ScreenshotMode Screenshot { get; init; }
    public int DelayMs { get; init; }
    public string? ScreenshotFile { get; init; }
    public string? OcrFile { get; init; }
    public IReadOnlyList<string> PinFiles { get; init; } = [];
    public IReadOnlyList<string> Files { get; init; } = [];
    public bool NewWindow { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];

    public bool IsEmpty => !ShowHelp && Screenshot == ScreenshotMode.None && ScreenshotFile == null &&
                           OcrFile == null && PinFiles.Count == 0 && Files.Count == 0;

    public const string HelpText = """
        Gradiwin — make your screenshots ready for the world.

        Usage: gradia [OPTIONS] [FILES...]

        Options:
          -h, --help                    Show this help message and exit
          --screenshot[=MODE]           Take a screenshot on startup
                                        MODE can be:
                                          INTERACTIVE (default) - Interactive screenshot
                                          FULL - Full screen screenshot
          --delay=MILLISECONDS          Delay before taking screenshot (in milliseconds)
          --screenshot-file=PATH        Open PATH as if it was a fresh screenshot
          --ocr-file=PATH               Recognize text in PATH and copy it to the clipboard
          --pin FILES...                Pin images in always-on-top windows
          --new-window                  Open a new window

        Arguments:
          FILES...                      Image files to open

        Examples:
          gradia                        Start normally
          gradia image.png              Open image.png
          gradia --screenshot           Take an interactive screenshot
          gradia --screenshot=FULL --delay=1500
        """;

    public static CommandLineOptions Parse(IReadOnlyList<string> args)
    {
        var files = new List<string>();
        var pins = new List<string>();
        var errors = new List<string>();
        var result = new CommandLineOptions();
        bool pinMode = false;

        for (int i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (string.IsNullOrEmpty(arg))
                continue;

            if (arg is "-h" or "--help" or "/?")
            {
                result = result with { ShowHelp = true };
                continue;
            }
            if (arg == "--new-window")
            {
                result = result with { NewWindow = true };
                continue;
            }
            if (arg == "--screenshot" || arg.StartsWith("--screenshot=", StringComparison.Ordinal))
            {
                var mode = arg.Contains('=') ? arg[(arg.IndexOf('=') + 1)..] : "INTERACTIVE";
                result = result with
                {
                    Screenshot = mode.ToUpperInvariant() switch
                    {
                        "FULL" => ScreenshotMode.Full,
                        "INTERACTIVE" or "" => ScreenshotMode.Interactive,
                        _ => ScreenshotMode.Interactive,
                    },
                };
                if (mode.ToUpperInvariant() is not ("FULL" or "INTERACTIVE" or ""))
                    errors.Add($"Unknown screenshot mode '{mode}', using INTERACTIVE.");
                continue;
            }
            if (TryValue(args, ref i, "--delay", out var delay))
            {
                if (int.TryParse(delay, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms) && ms >= 0)
                    result = result with { DelayMs = ms };
                else
                    errors.Add($"Invalid delay '{delay}'.");
                continue;
            }
            if (TryValue(args, ref i, "--screenshot-file", out var shot))
            {
                result = result with { ScreenshotFile = shot };
                continue;
            }
            if (TryValue(args, ref i, "--ocr-file", out var ocr))
            {
                result = result with { OcrFile = ocr };
                continue;
            }
            if (arg == "--pin")
            {
                pinMode = true;
                continue;
            }
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                errors.Add($"Unknown option '{arg}'.");
                continue;
            }

            if (pinMode)
                pins.Add(arg);
            else
                files.Add(arg);
        }

        return result with { Files = files, PinFiles = pins, Errors = errors };
    }

    private static bool TryValue(IReadOnlyList<string> args, ref int i, string name, out string value)
    {
        var arg = args[i];
        if (arg.StartsWith(name + "=", StringComparison.Ordinal))
        {
            value = arg[(name.Length + 1)..];
            return true;
        }
        if (arg == name && i + 1 < args.Count)
        {
            value = args[++i];
            return true;
        }
        value = "";
        return false;
    }

    /// <summary>Splits a raw Windows command line (as received by a redirected activation) into arguments.</summary>
    public static IReadOnlyList<string> SplitCommandLine(string commandLine)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;
        bool hasToken = false;
        for (int i = 0; i < commandLine.Length; i++)
        {
            char c = commandLine[i];
            if (c == '\\')
            {
                int slashes = 0;
                while (i < commandLine.Length && commandLine[i] == '\\') { slashes++; i++; }
                if (i < commandLine.Length && commandLine[i] == '"')
                {
                    current.Append('\\', slashes / 2);
                    if (slashes % 2 == 1) current.Append('"');
                    else { inQuotes = !inQuotes; }
                    hasToken = true;
                }
                else
                {
                    current.Append('\\', slashes);
                    i--;
                    hasToken = true;
                }
                continue;
            }
            if (c == '"')
            {
                inQuotes = !inQuotes;
                hasToken = true;
                continue;
            }
            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (hasToken)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }
                continue;
            }
            current.Append(c);
            hasToken = true;
        }
        if (hasToken)
            result.Add(current.ToString());
        return result;
    }
}
