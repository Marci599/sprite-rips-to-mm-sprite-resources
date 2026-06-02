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

        public static SKColor RotateSkColor(SKColor color, float degrees)
        {

            color.ToHsl(out float h, out float s, out float l);

 
            float newHue = (h + degrees) % 360f;
            if (newHue < 0) newHue += 360f;

            float flippedValue = 100f - l;

         
            return SKColor.FromHsl(newHue, s, flippedValue, color.Alpha);
        }
        public static SKBitmap CropBitmap(SKBitmap src, int left, int top, int width, int height)
        {
            var cropped = new SKBitmap(new SKImageInfo(width, height, src.ColorType, src.AlphaType));
            using (var canvas = new SKCanvas(cropped))
            {
                canvas.Clear(SKColors.Transparent);
                var sourceRect = new SKRect(left, top, left + width, top + height);
                var destRect = new SKRect(0, 0, width, height);
                canvas.DrawBitmap(src, sourceRect, destRect);
            }
            return cropped;
        }

        public static (int left, int top, int right, int bottom) RectTrimColor(SKBitmap src, SubjectConfig subjectConfig, (byte r, byte g, byte b, byte a)? parsedBackgroundColor)
        {
            IntVector2 size = new(src.Width, src.Height);
            var pixels = src.GetPixelSpan();

            bool trimByAlpha = subjectConfig.BackgroundColor == null || parsedBackgroundColor!.Value.a == 0 || subjectConfig.RemoveBackground;
            int left = size.X;
            int top = size.Y;
            int right = -1;
            int bottom = -1;
            

            double thr2 = subjectConfig.ColorTreshold * subjectConfig.ColorTreshold;
            byte tr = parsedBackgroundColor?.r ?? 0;
            byte tg = parsedBackgroundColor?.g ?? 0;
            byte tb = parsedBackgroundColor?.b ?? 0;
            byte ta = parsedBackgroundColor?.a ?? 0;

            for (int y = 0; y < size.Y; y++)
            {
                for (int x = 0; x < size.X; x++)
                {
                    int idx = (y * size.X + x) * 4;
                    byte b = pixels[idx + 0];
                    byte g = pixels[idx + 1];
                    byte r = pixels[idx + 2];
                    byte a = pixels[idx + 3];

                    bool keep;
                    if (trimByAlpha)
                    {
                        keep = a != 0;
                    }
                    else
                    {
                        int dr = r - tr;
                        int dg = g - tg;
                        int db = b - tb;
                        int da = a - ta;
                        long dist2 = (long)dr * dr + (long)dg * dg + (long)db * db + (long)da * da;
                        keep = dist2 > thr2;
                    }

                    if (!keep) continue;

             
                    if (x < left) left = x;
                    if (y < top) top = y;
                    if (x > right) right = x;
                    if (y > bottom) bottom = y;
                }
            }

            if (right == -1 || bottom == -1)
            {
    
                return (0, 0, size.X, size.Y);
            }

            right++;
            bottom++;
            return (left, top, right, bottom);
        }

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
