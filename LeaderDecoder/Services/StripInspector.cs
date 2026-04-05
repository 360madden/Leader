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
        public string ProfileName { get; init; } = "native-4x4";
    }

    /// <summary>
    /// Utility helpers for non-invasive inspection of the 28x4 telemetry strip.
    /// Produces both decoded state and raw sampled RGB values for each strip block.
    /// </summary>
    public static class StripInspector
    {
        private sealed class SampleProfile
        {
            public required string Name { get; init; }
            public required int[] SampleXs { get; init; }
            public int SampleY { get; init; }
        }

        private static readonly SampleProfile[] Profiles =
        {
            new SampleProfile { Name = "native-4x4", SampleXs = new[] { 2, 6, 10, 14, 18, 22, 26 }, SampleY = 2 },
            new SampleProfile { Name = "compact-2x2", SampleXs = new[] { 1, 3, 5, 7, 9, 11, 13 }, SampleY = 1 },
            new SampleProfile { Name = "compact-1x1", SampleXs = new[] { 0, 1, 2, 4, 5, 6, 7 }, SampleY = 0 },
        };

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
            var profile = ResolveProfile(normalized);

            var samples = new List<StripSample>(BlockCount);
            for (int pixelIndex = 0; pixelIndex < BlockCount; pixelIndex++)
            {
                int sampleX = Math.Min(profile.SampleXs[pixelIndex], Math.Max(0, normalized.Width - 1));
                int sampleY = Math.Min(profile.SampleY, Math.Max(0, normalized.Height - 1));
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
                ProfileName = profile.Name,
            };
        }

        public static string FormatSampleTable(StripAnalysis analysis)
        {
            ArgumentNullException.ThrowIfNull(analysis);

            var sb = new StringBuilder();
            sb.Append("Profile: ");
            sb.AppendLine(analysis.ProfileName);
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
                $"Motion heading: {state.RawFacing:F4} rad | ZoneHash={state.ZoneHash} | PlayerTag={state.PlayerTag}"
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

        private static SampleProfile ResolveProfile(Bitmap normalized)
        {
            for (int index = 0; index < Profiles.Length; index++)
            {
                var profile = Profiles[index];
                if (!ProfileFits(profile, normalized.Width, normalized.Height))
                {
                    continue;
                }

                Color sync = normalized.GetPixel(profile.SampleXs[0], profile.SampleY);
                if (sync.R >= 240 && sync.G <= 15 && sync.B >= 240)
                {
                    return profile;
                }
            }

            return Profiles[0];
        }

        private static bool ProfileFits(SampleProfile profile, int width, int height)
        {
            if (profile.SampleY < 0 || profile.SampleY >= height)
            {
                return false;
            }

            for (int index = 0; index < profile.SampleXs.Length; index++)
            {
                int sampleX = profile.SampleXs[index];
                if (sampleX < 0 || sampleX >= width)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
