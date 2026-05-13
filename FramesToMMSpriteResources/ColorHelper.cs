using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FramesToMMSpriteResources
{
    internal class ColorHelper
    {
        public static void RemoveColorWithThresholdInPlace(SKBitmap bitmap, byte r, byte g, byte b, byte a, int colorThreshold)
        {
            var pixels = bitmap.GetPixelSpan();
            int length = pixels.Length;

            for (int i = 0; i < length; i += 4)
            {
                byte pb = pixels[i + 0];
                byte pg = pixels[i + 1];
                byte pr = pixels[i + 2];
                byte pa = pixels[i + 3];

                if (IsWithinThreshold(pr, pg, pb, pa, r, g, b, a, colorThreshold))
                {
                    pixels[i + 3] = 0;
                }
            }

            bitmap.NotifyPixelsChanged();
        }

        public static bool IsWithinThreshold(byte pr, byte pg, byte pb, byte pa, byte r, byte g, byte b, byte a, int colorThreshold)
        {
            int dr = pr - r;
            int dg = pg - g;
            int db = pb - b;
            int da = pa - a;
            int dist2 = (dr * dr) + (dg * dg) + (db * db) + (da * da);
            int thresholdSquared = colorThreshold * colorThreshold;
            return dist2 <= thresholdSquared;
        }

        public static bool TryParse(string? input, out byte a, out byte r, out byte g, out byte b)
        {
            a = r = g = b = 0;

            if (string.IsNullOrWhiteSpace(input))
                return false;

            string s = input.Trim();

            if (s.StartsWith('#'))
                s = s[1..];

            if (s.Length != 6 && s.Length != 8)
                return false;

            if (!uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out uint value))
                return false;

            if (s.Length == 6)
            {
                r = (byte)((value >> 16) & 0xFF);
                g = (byte)((value >> 8) & 0xFF);
                b = (byte)(value & 0xFF);
                a = 255;
            }
            else // 8
            {
                r = (byte)((value >> 24) & 0xFF);
                g = (byte)((value >> 16) & 0xFF);
                b = (byte)((value >> 8) & 0xFF);
                a = (byte)(value & 0xFF);
            }

            return true;
        }
    }
}
