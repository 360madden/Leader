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
        // Centre sample offset within each 4-px block
        private const int S = 2;

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
                    // ── Pixel 0 @ (0+S, S): Sync Magenta check ──────────────
                    var sync = ReadAt(0 * 4 + S, S);
                    if (sync.R < 240 || sync.G > 15 || sync.B < 240)
                        return new GameState { IsValid = false };

                    var state = new GameState { IsValid = true };

                    // ── Pixel 1 @ (4+S, S): Status ──────────────────────────
                    var p1 = ReadAt(1 * 4 + S, S);
                    state.PlayerHP  = p1.R;
                    state.TargetHP  = p1.G;
                    int flags       = p1.B;
                    state.IsCombat  = (flags & 0x01) != 0;
                    state.HasTarget = (flags & 0x02) != 0;
                    state.IsMoving  = (flags & 0x04) != 0;
                    state.IsAlive   = (flags & 0x08) != 0;
                    state.IsMounted = (flags & 0x10) != 0;

                    // ── Pixels 2-4: Coordinates ──────────────────────────────
                    state.CoordX = DecodeCoord(ReadAt(2 * 4 + S, S));
                    state.CoordY = DecodeCoord(ReadAt(3 * 4 + S, S));
                    state.CoordZ = DecodeCoord(ReadAt(4 * 4 + S, S));

                    // ── Pixel 5 @ (20+S, S): Heading + Zone ──────────────────
                    var p5 = ReadAt(5 * 4 + S, S);
                    state.RawFacing = (p5.R + p5.G * 256) / 10000.0f;
                    state.ZoneHash  = p5.B;

                    // ── Pixel 6 @ (24+S, S): Target hash ─────────────────────
                    var p6 = ReadAt(6 * 4 + S, S);
                    state.TargetHash = $"{p6.R:X2}{p6.G:X2}{p6.B:X2}";

                    return state;
                }
            }
            finally
            {
                bmp.UnlockBits(bd);
            }
        }

        private static float DecodeCoord(Color c) =>
            (c.R + c.G * 256 + c.B * 65536 - 8388608) / 10.0f;
    }
}
