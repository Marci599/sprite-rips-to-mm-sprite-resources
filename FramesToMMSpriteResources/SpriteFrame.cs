using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FramesToMMSpriteResources
{
    public class SpriteFrame
    {
        public SKBitmap WriteableBitmap;
        public SKRectI CroppedRect;
        public IntVector2 OriginalSize;

        public SpriteFrame(SKBitmap writeableBitmap, SKRectI croppedRect, IntVector2 originalSize)
        {
            WriteableBitmap = writeableBitmap;
            CroppedRect = croppedRect;
            OriginalSize = originalSize;
        }
    }
}
