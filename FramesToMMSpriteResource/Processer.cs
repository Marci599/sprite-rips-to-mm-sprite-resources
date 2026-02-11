using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.Effects;



namespace FramesToMMSpriteResource
{
    class Processer
    {
        private static readonly CanvasDevice SharedCanvasDevice = new CanvasDevice();

        static public string? StartProcess()
        {
            ProgramConfig programConfig = MainWindow.programConfig;
            GameThemeConfig gameThemeConfig = programConfig.GameThemeConfigs[programConfig.SelectedNode[0]];
            SubjectConfig subjectConfig = gameThemeConfig.SubjectConfigs[programConfig.SelectedNode[1]];
            string subjectPath = MainWindow.usingGameThemes ?
            
                Path.Combine(MainWindow.workingPath, programConfig.SelectedNode[0], programConfig.SelectedNode[1])
            :
            
                Path.Combine(MainWindow.workingPath, programConfig.SelectedNode[1]);
            

            foreach (var (animationName, animationConfig) in subjectConfig.AnimationConfigs)
            {
                string animationPath = Path.Combine(subjectPath, "raw", animationName);
                foreach(string spritePath in Directory.GetDirectories(animationPath))
                {
                    var image = CanvasBitmap.LoadAsync(SharedCanvasDevice, spritePath).GetAwaiter().GetResult();
                    if (!string.IsNullOrEmpty(subjectConfig.BackgroundColor) && subjectConfig.RemoveBackground)
                    {
                        image = RemoveColorWithThreshold(image);
                    }

                    if (subjectConfig.ResizeToPercent != 100 && subjectConfig.ResizeToPercent > 0)
                    {
                        var scale = subjectConfig.ResizeToPercent / 100.0;
                        int newW = Math.Max(1, (int)Math.Round(image.SizeInPixels.Width * scale));
                        int newH = Math.Max(1, (int)Math.Round(image.SizeInPixels.Height * scale));
                        if (newW != image.SizeInPixels.Width || newH != image.SizeInPixels.Height)
                            image = ResizeBitmapNearest(image, newW, newH);
                    }

                    var originalSize = (image.SizeInPixels.Width, image.SizeInPixels.Height);
                    (int left, int top) trimOffset = (0, 0);
                    (CanvasBitmap imgAfterTrim, (int left, int top) off) = (image, (0, 0));
                 
                    if (subjectConfig.CropSprites)
                    {
                        (imgAfterTrim, off) = TrimColor(image);
                    }
                    image = imgAfterTrim;
                    if (gameThemeConfig.IsHd)
                    {
                        image = EnsureEvenDimensions(image);
                    }

       
                }
            }
            return null;
        }

        static CanvasBitmap RemoveColorWithThreshold(CanvasBitmap image)
        {
            return image;
        }

        static CanvasBitmap ResizeBitmapNearest(CanvasBitmap image, int newWidth, int newHeight)
        {
            return image;
        }

        static (CanvasBitmap cropped, (int left, int top) offset) TrimColor(CanvasBitmap image)
        {
            return (image, (0, 0));
        }

        static CanvasBitmap EnsureEvenDimensions(CanvasBitmap image)
        {
            return image;
        }
    }
}
