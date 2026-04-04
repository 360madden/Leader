using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using LeaderDecoder.Models;

namespace LeaderDecoder.Services
{
    public sealed class StripSample
    {
        public int PixelIndex { get; set; }
        public int SampleX { get; set; }
        public int SampleY { get; set; }
        public int R { get; set; }
        public int G { get; set; }
        public int B { get; set; }
    }

    public sealed class StripAnalysis
    {
        public required GameState State { get; init; }
        public required IReadOnlyList<StripSample> Samples { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
    }

    /// <summary>
    /// Utility helpers for non-invasive inspection of the 28x4 telemetry strip.
    /// Produces both decoded state and raw sampled RGB values for each strip block.
    /// </summary>
    public static class StripInspector
    {
        public const int BlockCount = 7;
        public const int BlockWidth = 4;
        public const int StripWidth = 28;
        public const int StripHeight = 4;
        public const int SampleOffset = 2;

        public static StripAnalysis Analyze(Bitmap source)
        {
            ArgumentNullException.ThrowIfNull(source);

            using var normalized = NormalizeToArgb32(source);
            var telemetry = new TelemetryService();
            var state = telemetry.Decode(normalized);
            int sampleY = Math.Min(SampleOffset, Math.Max(0, normalized.Height - 1));

            var samples = new List<StripSample>(BlockCount);
            for (int pixelIndex = 0; pixelIndex < BlockCount; pixelIndex++)
            {
                int sampleX = Math.Min(pixelIndex * BlockWidth + SampleOffset, Math.Max(0, normalized.Width - 1));
                Color c = normalized.GetPixel(sampleX, sampleY);

                samples.Add(new StripSample
                {
                    PixelIndex = pixelIndex,
                    SampleX = sampleX,
                    SampleY = sampleY,
                    R = c.R,
                    G = c.G,
                    B = c.B,
                });
            }

            return new StripAnalysis
            {
                State = state,
                Samples = samples,
                Width = normalized.Width,
                Height = normalized.Height,
            };
        }

        public static string FormatSampleTable(StripAnalysis analysis)
        {
            ArgumentNullException.ThrowIfNull(analysis);

            var sb = new StringBuilder();
            sb.AppendLine("Pixel  Sample  RGB");
            sb.AppendLine("-----  ------  ----------------");

            foreach (var sample in analysis.Samples)
            {
                sb.Append("P");
                sb.Append(sample.PixelIndex);
                sb.Append("     ");
                sb.Append(sample.SampleX.ToString().PadLeft(2));
                sb.Append(",");
                sb.Append(sample.SampleY);
                sb.Append("    ");
                sb.Append(sample.R.ToString().PadLeft(3));
                sb.Append(", ");
                sb.Append(sample.G.ToString().PadLeft(3));
                sb.Append(", ");
                sb.Append(sample.B.ToString().PadLeft(3));
                sb.AppendLine();
            }

            return sb.ToString().TrimEnd();
        }

        public static string FormatStateSummary(GameState state)
        {
            ArgumentNullException.ThrowIfNull(state);

            if (!state.IsValid)
            {
                return "Decoded state: INVALID (no sync magenta detected)";
            }

            return string.Join(Environment.NewLine, new[]
            {
                $"Decoded state: VALID | HP={state.PlayerHP} TargetHP={state.TargetHP} Flags=" +
                $"{(state.IsCombat ? "CB " : "")}{(state.HasTarget ? "TGT " : "")}{(state.IsMoving ? "MOV " : "")}{(state.IsAlive ? "ALIVE " : "")}{(state.IsMounted ? "MT" : "")}".Trim(),
                $"Coords: X={state.CoordX:F1} Y={state.CoordY:F1} Z={state.CoordZ:F1}",
                $"Facing: {state.RawFacing:F4} rad | ZoneHash={state.ZoneHash} | TargetHash={state.TargetHash}"
            });
        }

        private static Bitmap NormalizeToArgb32(Bitmap source)
        {
            if (source.PixelFormat == PixelFormat.Format32bppArgb)
            {
                return (Bitmap)source.Clone();
            }

            var copy = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(copy))
            {
                g.DrawImage(source, 0, 0, source.Width, source.Height);
            }

            return copy;
        }
    }
}
