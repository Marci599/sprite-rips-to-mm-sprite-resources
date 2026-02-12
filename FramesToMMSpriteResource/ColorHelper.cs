using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FramesToMMSpriteResource
{
    internal class ColorHelper
    {
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
