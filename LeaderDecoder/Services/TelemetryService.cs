using System;
using System.Drawing;
using LeaderDecoder.Models;

namespace LeaderDecoder.Services
{
    /// <summary>
    /// LEADER TELEMETRY DECODER v1.0
    /// Inverse logic of Modules/Encoder.lua.
    /// Extracts spatial and status data from the optical telemetry strip.
    /// </summary>
    public class TelemetryService
    {
        private const int PixelSize = 4;

        /// <summary>
        /// Decodes a telemetry bitmap into a GameState object.
        /// </summary>
        /// <param name="bmp">Captured telemetry strip bitmap.</param>
        /// <returns>A GameState object with decoded telemetry data.</returns>
        public GameState Decode(Bitmap bmp)
        {
            if (bmp == null || bmp.Width < 28 || bmp.Height < 4) 
                return new GameState { IsValid = false };

            // 1. Sync Validation (Pixel 0: Static Magenta 255, 0, 255)
            Color sync = bmp.GetPixel(2, 2);
            if (sync.R < 250 || sync.G > 5 || sync.B < 250) 
            {
                return new GameState { IsValid = false };
            }

            var state = new GameState { IsValid = true };

            // 2. Decode Status (Pixel 1 @ Center 6, 2)
            Color p1 = bmp.GetPixel(6, 2);
            state.PlayerHP = p1.R;
            state.TargetHP = p1.G;

            int flags = p1.B;
            state.IsCombat = (flags & 1) != 0;
            state.HasTarget = (flags & 2) != 0;
            state.IsMoving = (flags & 4) != 0;
            state.IsAlive = (flags & 8) != 0;
            state.IsMounted = (flags & 16) != 0;

            // 3. Decode Coords (Pixels 2, 3, 4 @ Center 10/14/18, 2)
            // Packed 24-bit (R=Low, G=Mid, B=High) | Math: (val * 10) + 8388608
            
            Color px = bmp.GetPixel(10, 2);
            state.CoordX = (px.R + (px.G * 256) + (px.B * 65536) - 8388608) / 10.0f;

            Color py = bmp.GetPixel(14, 2);
            state.CoordY = (py.R + (py.G * 256) + (py.B * 65536) - 8388608) / 10.0f;

            Color pz = bmp.GetPixel(18, 2);
            state.CoordZ = (pz.R + (pz.G * 256) + (pz.B * 65536) - 8388608) / 10.0f;

            // 4. Decode Heading & Zone (Pixel 5 @ Center 22, 2)
            Color ph = bmp.GetPixel(22, 2);
            state.RawFacing = (ph.R + (ph.G * 256)) / 10000.0f;
            state.ZoneHash = ph.B;

            // 5. Decode Target Hash (Pixel 6 @ Center 26, 2)
            Color pt = bmp.GetPixel(26, 2);
            state.TargetHash = $"{pt.R:X2}{pt.G:X2}{pt.B:X2}";

            return state;
        }
    }
}
