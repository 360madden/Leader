using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using LeaderDecoder.Services;

namespace LeaderScreenshotInspector
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            string? imagePath = ResolveImagePath(args);
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            {
                Console.Error.WriteLine("No screenshot path was provided and no latest screenshot could be found.");
                return 1;
            }

            bool saveCrop = args.Any(arg => string.Equals(arg, "--save-crop", StringComparison.OrdinalIgnoreCase));

            Console.Title = "Leader Screenshot Inspector";
            Console.WriteLine("============================================================");
            Console.WriteLine("Leader Screenshot Inspector (non-invasive)");
            Console.WriteLine("Inspects the top-left 28x4 strip inside a saved screenshot.");
            Console.WriteLine("============================================================");
            Console.WriteLine($"Image: {imagePath}");

            using var bmp = (Bitmap)Image.FromFile(imagePath);
            Console.WriteLine($"Dimensions: {bmp.Width}x{bmp.Height}");
            Console.WriteLine($"Timestamp: {File.GetLastWriteTime(imagePath):yyyy-MM-dd HH:mm:ss}");

            var analysis = StripInspector.Analyze(bmp);
            Console.WriteLine(StripInspector.FormatStateSummary(analysis.State));
            Console.WriteLine(StripInspector.FormatSampleTable(analysis));

            if (saveCrop)
            {
                using var crop = bmp.Clone(new Rectangle(0, 0, Math.Min(StripInspector.StripWidth, bmp.Width), Math.Min(StripInspector.StripHeight, bmp.Height)), bmp.PixelFormat);
                string outputPath = Path.Combine(Path.GetDirectoryName(imagePath) ?? ".", $"{Path.GetFileNameWithoutExtension(imagePath)}_strip.png");
                SaveBitmapCopy(crop, outputPath);
                Console.WriteLine($"Saved crop: {outputPath}");
            }

            return 0;
        }

        private static string? ResolveImagePath(string[] args)
        {
            string? explicitPath = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(explicitPath))
            {
                return Path.GetFullPath(explicitPath);
            }

            string screenshotsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "RIFT",
                "Screenshots");

            if (!Directory.Exists(screenshotsPath))
            {
                return null;
            }

            return Directory.GetFiles(screenshotsPath)
                .OrderByDescending(File.GetLastWriteTime)
                .FirstOrDefault();
        }

        private static void SaveBitmapCopy(Bitmap source, string outputPath)
        {
            using var copy = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(copy);
            g.DrawImage(source, 0, 0, source.Width, source.Height);
            copy.Save(outputPath, ImageFormat.Png);
        }
    }
}
