using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using LeaderDecoder.Models;

namespace LeaderDecoder.Services
{
    /// <summary>
    /// LEADER TELEMETRY DECODER v1.1
    /// Uses LockBits for a direct memory scan — ~10x faster than GetPixel.
    /// Pixel sampling reads from the centre of each 4×4 block (offset 2,2).
    /// </summary>
    public class TelemetryService
    {
        private const string PlayerTagAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_";
        private const int PlayerTagLength = 4;
        private const int PlayerTagBase = 37;

        private static readonly SampleProfile[] Profiles =
        {
            new SampleProfile("native-4x4", new[] { 2, 6, 10, 14, 18, 22, 26 }, 2),
            new SampleProfile("compact-2x2", new[] { 1, 3, 5, 7, 9, 11, 13 }, 1),
            new SampleProfile("compact-1x1", new[] { 0, 1, 2, 4, 5, 6, 7 }, 0),
        };

        private readonly record struct SampleProfile(string Name, int[] SampleXs, int SampleY);

        /// <summary>Decodes a telemetry bitmap into a GameState.</summary>
        public GameState Decode(Bitmap bmp)
        {
            if (bmp == null || bmp.Width < 28 || bmp.Height < 4)
                return new GameState { IsValid = false };

            // Lock entire strip into unmanaged memory for fast sequential reads
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            BitmapData bd = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            try
            {
                // stride = bytes per row; pixels are BGRA in memory
                int stride = bd.Stride;
                IntPtr ptr = bd.Scan0;

                // Helper: read BGRA at (px, py)
                unsafe byte* Row(int py) => (byte*)ptr + py * stride;

                unsafe Color ReadAt(int px, int py)
                {
                    byte* p = Row(py) + px * 4;
                    return Color.FromArgb(p[2], p[1], p[0]); // R=p[2], G=p[1], B=p[0]
                }

                unsafe
                {
                    foreach (var profile in Profiles)
                    {
                        if (!ProfileFits(profile, bmp.Width, bmp.Height))
                            continue;

                        var sync = ReadAt(profile.SampleXs[0], profile.SampleY);
                        if (sync.R < 240 || sync.G > 15 || sync.B < 240)
                            continue;

                        var state = new GameState { IsValid = true };

                        var p1 = ReadAt(profile.SampleXs[1], profile.SampleY);
                        state.PlayerHP = p1.R;
                        state.TargetHP = p1.G;
                        int flags = p1.B;
                        state.IsCombat = (flags & 0x01) != 0;
                        state.HasTarget = (flags & 0x02) != 0;
                        state.IsMoving = (flags & 0x04) != 0;
                        state.IsAlive = (flags & 0x08) != 0;
                        state.IsMounted = (flags & 0x10) != 0;

                        state.CoordX = DecodeCoord(ReadAt(profile.SampleXs[2], profile.SampleY));
                        state.CoordY = DecodeCoord(ReadAt(profile.SampleXs[3], profile.SampleY));
                        state.CoordZ = DecodeCoord(ReadAt(profile.SampleXs[4], profile.SampleY));

                        var p5 = ReadAt(profile.SampleXs[5], profile.SampleY);
                        state.RawFacing = (p5.R + p5.G * 256) / 10000.0f;
                        state.ZoneHash = p5.B;

                        var p6 = ReadAt(profile.SampleXs[6], profile.SampleY);
                        state.PlayerTag = DecodePlayerTag(p6);

                        return state;
                    }

                    return new GameState { IsValid = false };
                }
            }
            finally
            {
                bmp.UnlockBits(bd);
            }
        }

        public static string DecodePlayerTag(Color c)
        {
            int packed = c.R + c.G * 256 + c.B * 65536;
            char[] chars = new char[PlayerTagLength];

            for (int index = PlayerTagLength - 1; index >= 0; index--)
            {
                int value = packed % PlayerTagBase;
                packed /= PlayerTagBase;
                chars[index] = PlayerTagAlphabet[value];
            }

            return new string(chars);
        }

        public static Color EncodePlayerTag(string? tag)
        {
            string normalized = NormalizePlayerTag(tag);
            int packed = 0;

            for (int index = 0; index < normalized.Length; index++)
            {
                int value = PlayerTagAlphabet.IndexOf(normalized[index]);
                if (value < 0)
                {
                    value = PlayerTagAlphabet.Length - 1;
                }

                packed = packed * PlayerTagBase + value;
            }

            return Color.FromArgb(packed & 0xFF, (packed >> 8) & 0xFF, (packed >> 16) & 0xFF);
        }

        private static string NormalizePlayerTag(string? tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                return "____";
            }

            char[] chars = new char[PlayerTagLength];
            int writeIndex = 0;

            foreach (char raw in tag.ToUpperInvariant())
            {
                if ((raw >= 'A' && raw <= 'Z') || (raw >= '0' && raw <= '9') || raw == '_')
                {
                    chars[writeIndex++] = raw;
                }

                if (writeIndex == PlayerTagLength)
                {
                    break;
                }
            }

            while (writeIndex < PlayerTagLength)
            {
                chars[writeIndex++] = '_';
            }

            return new string(chars);
        }

        private static bool ProfileFits(SampleProfile profile, int width, int height)
        {
            if (profile.SampleY < 0 || profile.SampleY >= height)
                return false;

            for (int index = 0; index < profile.SampleXs.Length; index++)
            {
                int sampleX = profile.SampleXs[index];
                if (sampleX < 0 || sampleX >= width)
                    return false;
            }

            return true;
        }

        private static float DecodeCoord(Color c) =>
            (c.R + c.G * 256 + c.B * 65536 - 8388608) / 10.0f;
    }
}
