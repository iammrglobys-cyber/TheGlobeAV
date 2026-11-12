using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TheGlobeAV.Models;

namespace TheGlobeAV.Services;

public sealed class ReplayGifExporter
{
    private sealed record Step(
        string Time,
        string Category,
        string Message);

    public string CreateInspectionReplay(InspectionRecord record)
    {
        var steps = record.Events.Select(e => new Step(
            $"{e.OffsetMilliseconds:N0} ms",
            e.Category,
            e.Message)).ToList();

        return CreateGif(
            "FILE INSPECTION REPLAY",
            record.FileName,
            steps);
    }

    public string CreateFullScanReplay(string activityPath)
    {
        var steps = new List<Step>();

        if (File.Exists(activityPath))
        {
            foreach (string line in File.ReadLines(activityPath))
            {
                if (steps.Count >= 30)
                    break;

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                steps.Add(new Step(
                    "",
                    "GLOBEAV ACTIVITY",
                    line));
            }
        }

        return CreateGif(
            "FULL PC DISCOVERY REPLAY",
            "Recorded folder traversal",
            steps);
    }

    private static string CreateGif(
        string title,
        string subtitle,
        List<Step> steps)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "GIF rendering requires Windows.");

        if (steps.Count == 0)
        {
            steps.Add(new Step(
                "0 ms",
                "INFO",
                "No replay events were recorded."));
        }

        string directory = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "TheGlobeAV",
            "ScanHistory");

        Directory.CreateDirectory(directory);

        string path = Path.Combine(
            directory,
            $"replay-{Guid.NewGuid():N}.gif");

        var encoder = new GifBitmapEncoder();

        const int width = 900;
        const int height = 500;

        int index = 0;

        foreach (var step in steps.Take(30))
        {
            index++;

            var visual = new DrawingVisual();

            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(
                    new SolidColorBrush(Color.FromRgb(9, 13, 24)),
                    null,
                    new Rect(0, 0, width, height));

                dc.DrawRoundedRectangle(
                    new SolidColorBrush(Color.FromRgb(17, 24, 39)),
                    new Pen(
                        new SolidColorBrush(Color.FromRgb(43, 67, 99)),
                        2),
                    new Rect(25, 25, 850, 450),
                    18, 18);

                DrawText(dc, "THEGLOBEAV",
                    16, Brushes.DeepSkyBlue, 55, 50, 700);

                DrawText(dc, title,
                    25, Brushes.White, 55, 95, 780);

                DrawText(dc, subtitle,
                    15, Brushes.LightSteelBlue, 55, 140, 780);

                dc.DrawRoundedRectangle(
                    new SolidColorBrush(Color.FromRgb(35, 49, 70)),
                    null,
                    new Rect(55, 200, 790, 8),
                    4, 4);

                dc.DrawRoundedRectangle(
                    new SolidColorBrush(Color.FromRgb(48, 135, 255)),
                    null,
                    new Rect(55, 200, 790.0 * index / steps.Count, 8),
                    4, 4);

                DrawText(dc,
                    $"EVENT {index} / {steps.Count}",
                    14, Brushes.DeepSkyBlue, 55, 235, 300);

                DrawText(dc,
                    step.Category,
                    20, Brushes.LightGreen, 55, 275, 780);

                DrawText(dc,
                    step.Message,
                    18, Brushes.White, 55, 320, 770, 90);

                DrawText(dc,
                    step.Time,
                    13, Brushes.LightSteelBlue, 55, 425, 350);

                DrawText(dc,
                    "Recorded GlobeAV events — not Defender's private internal activity.",
                    11, Brushes.Gray, 55, 450, 780);
            }

            var bitmap = new RenderTargetBitmap(
                width, height, 96, 96, PixelFormats.Pbgra32);

            bitmap.Render(visual);

            var metadata = new BitmapMetadata("gif");
            metadata.SetQuery("/grctlext/Delay", (ushort)75);
            metadata.SetQuery("/grctlext/Disposal", (byte)2);

            encoder.Frames.Add(
                BitmapFrame.Create(bitmap, null, metadata, null));
        }

        using var output = new FileStream(
            path, FileMode.Create, FileAccess.Write);

        encoder.Save(output);

        return path;
    }

    private static void DrawText(
        DrawingContext dc,
        string value,
        double size,
        Brush brush,
        double x,
        double y,
        double maxWidth,
        double maxHeight = 35)
    {
        var text = new FormattedText(
            value,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            size,
            brush,
            1.0)
        {
            MaxTextWidth = maxWidth,
            MaxTextHeight = maxHeight,
            Trimming = TextTrimming.CharacterEllipsis
        };

        dc.DrawText(text, new Point(x, y));
    }
}
