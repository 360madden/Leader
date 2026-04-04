using System;
using System.Drawing;
using LeaderDecoder.Models;

namespace LeaderDecoder.Services
{
    /// <summary>
    /// LEADER TELEMETRY SIMULATOR
    /// Generates synthetic bit-accurate 7-pixel strips for offline testing.
    /// Matches the encoding logic of Encoder.lua v1.1.
    /// </summary>
    public class TelemetrySimulator
    {
        public Bitmap Generate(GameState state)
        {
            var bmp = new Bitmap(28, 4);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Black);
            }

            // Pixel 0: Sync (Magenta)
            SetSimPixel(bmp, 0, Color.FromArgb(255, 0, 255));

            // Pixel 1: Status & Flags
            // Lua Code: Pack(playerHP, targetHP, flags)
            SetSimPixel(bmp, 1, Color.FromArgb(state.PlayerHP, state.TargetHP, EncodeFlags(state)));

            // Pixel 2: Coord X (24-bit)
            SetSimPixel(bmp, 2, EncodeCoord(state.CoordX));

            // Pixel 3: Coord Y (24-bit)
            SetSimPixel(bmp, 3, EncodeCoord(state.CoordY));

            // Pixel 4: Coord Z (24-bit)
            SetSimPixel(bmp, 4, EncodeCoord(state.CoordZ));

            // Pixel 5: Facing (Radians) & Zone
            SetSimPixel(bmp, 5, EncodeFacing(state));

            // Pixel 6: Reserved
            SetSimPixel(bmp, 6, Color.Black);

            return bmp;
        }

        private void SetSimPixel(Bitmap bmp, int index, Color color)
        {
            int startX = index * 4;
            // Fill a 4x4 block to simulate the RIFT renderer
            for (int x = startX; x < startX + 4; x++)
            {
                for (int y = 0; y < 4; y++)
                {
                    bmp.SetPixel(x, y, color);
                }
            }
        }

        private int EncodeFlags(GameState state)
        {
            int flags = 0;
            if (state.IsCombat) flags |= 1;
            if (state.HasTarget) flags |= 2;
            if (state.IsMoving) flags |= 4;
            if (state.IsAlive) flags |= 8;
            if (state.IsMounted) flags |= 16;
            return flags;
        }

        private Color EncodeCoord(float val)
        {
            int packed = (int)Math.Round(val * 10) + 8388608;
            return Color.FromArgb(packed & 0xFF, (packed >> 8) & 0xFF, (packed >> 16) & 0xFF);
        }

        private Color EncodeFacing(GameState state)
        {
            // Must match Encoder.lua v1.1: floor(radian * 10000), R=low, G=high, B=zoneHash
            int packed = (int)Math.Floor(state.RawFacing * 10000.0);
            packed = Math.Max(0, Math.Min(65535, packed));
            return Color.FromArgb(packed & 0xFF, (packed >> 8) & 0xFF, state.ZoneHash);
        }
    }
}
