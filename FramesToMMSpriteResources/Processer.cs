using NetVips;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace FramesToMMSpriteResources
{
    class ProcessedSprite(Image image, IntVector2 originalSize, IntVector2 trimOffset, string animationName, JsonObject? oldFrameJson)
    {
        public Image Image = image;
        public IntVector2 OriginalSize = originalSize;
        public IntVector2 TrimOffset = trimOffset;
        public string AnimationName = animationName;
        public JsonObject? OldFrameJson = oldFrameJson;
    }

    class LayoutInfo(IntVector2 layoutSize, IntVector2 canvasSize, List<IntVector2?> positions)
    {
        public IntVector2 LayoutSize = layoutSize;
        public IntVector2 CanvasSize = canvasSize;
        public List<IntVector2?> Positions = positions;
    }

    public class PreviousSpriteFileValues
    {
        public Dictionary<string, List<JsonObject>> FramesByAnimation { get; set; }
        public string SubPositions { get; set; }
    }

    class Processer
    {
        private const double MaxColorDistance = 441.6729559300637;
        static ProgramConfig programConfig;
        static GameThemeConfig gameThemeConfig;
        static SubjectConfig subjectConfig;
        static (byte r, byte g, byte b, byte a)? parsedBackgroundColor = null;

        public static async Task StartProcessAsync()
        {
            programConfig = MainWindow.programConfig;
            gameThemeConfig = programConfig.GameThemeConfigs[programConfig.SelectedNode[0]];
            subjectConfig = gameThemeConfig.SubjectConfigs[programConfig.SelectedNode[1]];

            if (subjectConfig.BackgroundColor != null)
            {
                ColorHelper.TryParse(subjectConfig.BackgroundColor, out byte a, out byte r, out byte g, out byte b);
                parsedBackgroundColor = (r, g, b, a);
            }


            string subjectPath = MainWindow.usingGameThemes ?

                Path.Combine(MainWindow.workingPath, programConfig.SelectedNode[0], programConfig.SelectedNode[1])
            :

                Path.Combine(MainWindow.workingPath, programConfig.SelectedNode[1]);

            List<ProcessedSprite> processedSprites = [];
            List<Dictionary<string, object>> animationsMeta = [];
            int frameIndex = 0;



            string subPositions = string.Empty;
            PreviousSpriteFileValues previousSpriteFile = null;

            string outputDir = Path.Combine(subjectPath, "generated");
            string spriteFilePath = Path.Combine(outputDir, programConfig.SelectedNode[1] + ".sprite");

            JsonArray frames = null;
            JsonArray named = null;

            if (File.Exists(spriteFilePath))
            {

                var txt = File.ReadAllText(spriteFilePath);
                var spritePayload = JsonNode.Parse(txt)?.AsObject();

                frames = spritePayload?["Frames"]?.AsArray();
                named = spritePayload?["NamedAnimations"]?.AsArray();


                var result = new PreviousSpriteFileValues
                {
                    FramesByAnimation = new Dictionary<string, List<JsonObject>>(),
                    SubPositions = spritePayload?["SubPositions"]?.GetValue<string>()
                };



            }



            foreach (var (animationName, animationConfig) in subjectConfig.AnimationConfigs)
            {
                List<JsonObject>? list = null;
                if (named != null && !animationConfig.Regenerate)
                {
                    foreach (var entryNode in named)
                    {
                        var entry = entryNode?.AsObject();
                        if (entry == null) continue;

                        var name = entry["Name"]?.GetValue<string>();
                        if (name == null || animationName != name) continue;


                        var framesField = entry["Frames"]?.GetValue<string>() ?? string.Empty;

                        var indices = framesField
                            .Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => int.Parse(s.Trim()))
                            .ToList();

                        list = new List<JsonObject>();

                        foreach (var idx in indices)
                        {
                            if (idx < 0 || idx >= frames.Count) continue;

                            if (frames[idx] is JsonObject frameObj)
                                list.Add(frameObj);

                        }
                        break;

                    }
                }


                int spritesCount = 0;
                string animationPath = Path.Combine(subjectPath, "raw", animationName);

                var filesInAnimationPath = Directory.GetFiles(animationPath);
                var i = 0;
                foreach (var spritePath in filesInAnimationPath)
                {

                    if (Path.GetExtension(spritePath) == ".png")
                    {

                        var image = LoadImage(spritePath);
                        if (!string.IsNullOrEmpty(subjectConfig.BackgroundColor) && subjectConfig.RemoveBackground)
                        {
                            image = RemoveColorWithThreshold(image);
                        }

                        if (subjectConfig.ResizeToPercent != 100 && subjectConfig.ResizeToPercent > 0)
                        {
                            var scale = subjectConfig.ResizeToPercent / 100.0;
                            int newW = Math.Max(1, (int)Math.Round(image.Width * scale));
                            int newH = Math.Max(1, (int)Math.Round(image.Height * scale));
                            if (newW != image.Width || newH != image.Height)
                                image = ResizeBitmapNearest(image, new IntVector2(newW, newH));
                        }

                        var originalSize = new IntVector2(image.Width, image.Height);

                        (Image imgAfterTrim, IntVector2 offset) = (image, new(0, 0));

                        if (subjectConfig.CropSprites)
                        {
                            (imgAfterTrim, offset) = TrimColor(image);
                        }
                        image = imgAfterTrim;
                        if (gameThemeConfig.IsHd)
                        {
                            image = EnsureEvenDimensions(image);
                        }

                        JsonObject oldJson = null;
                        if (list != null)
                        {
                            oldJson = list[i];
                        }
                        processedSprites.Add(new ProcessedSprite(image, originalSize, offset, animationName, oldJson));
                        spritesCount++;
                        i++;
                    }
                }
                var frameRange = Enumerable.Range(frameIndex, spritesCount).ToList();
                animationsMeta.Add(new Dictionary<string, object>
                {
                    ["name"] = animationName,
                    ["frames"] = frameRange,
                    ["delay"] = animationConfig.Delay
                });
                frameIndex += spritesCount;
            }

            var layoutInfo = SelectLayout(processedSprites);
            var finalPositions = layoutInfo.Positions;
            if (finalPositions.Any(p => p is null))
                throw new InvalidOperationException("Failed to generate positions for every sprite.");

            var canvasSize = new IntVector2(layoutInfo.CanvasSize.X, layoutInfo.CanvasSize.Y);

            var sheetImage = CreateSpriteSheet(processedSprites, finalPositions, canvasSize);



            var payload = ExportSpriteMetadata(processedSprites, finalPositions, canvasSize, animationsMeta, subPositions);



            if (Directory.Exists(outputDir))
            {
                foreach (var child in Directory.EnumerateFiles(outputDir))
                {
                    try { File.Delete(child); } catch (Exception ex)
                    {
                        Debug.WriteLine(ex.Message); Debug.WriteLine(ex.StackTrace);
                    }
                }
            }
            Directory.CreateDirectory(outputDir);

            var spritesheetPath = Path.Combine(outputDir, programConfig.SelectedNode[1] + ".png");
            var spritesheetPath2x = spritesheetPath;
            if (gameThemeConfig.IsHd)
            {
                int halfW = Math.Max(1, (sheetImage.Width + 1) / 2);
                int halfH = Math.Max(1, (sheetImage.Height + 1) / 2);
                var sheetHalf = ResizeBitmapNearest(sheetImage, new IntVector2(halfW, halfH));
                SaveImageToFile(sheetHalf, spritesheetPath);
                spritesheetPath2x = Path.Combine(outputDir, programConfig.SelectedNode[1] + "@2x.png");
            }
            SaveImageToFile(sheetImage, spritesheetPath2x);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            File.WriteAllText(spriteFilePath, payload.ToJsonString(options));
            await Task.CompletedTask;
        }

        private static Image LoadImage(string path)
        {
            var image = Image.NewFromFile(path, access: Enums.Access.Sequential);
            if (image.Bands == 4) return image;
            if (image.Bands == 3)
            {
                var alpha = Image.Black(image.Width, image.Height) + 255;
                return image.Bandjoin(alpha);
            }
            throw new InvalidOperationException($"Unsupported band count: {image.Bands} in {path}");
        }

        private static void SaveImageToFile(Image image, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            image.WriteToFile(path);
        }


        private static JsonObject ExportSpriteMetadata(List<ProcessedSprite> sprites, List<IntVector2?> positions, IntVector2 canvasSize, List<Dictionary<string, object>> animations, string subPositions)
        {
            int sourceWidth = canvasSize.X;
            int sourceHeight = canvasSize.Y;
            int targetWidth = sourceWidth / 2;
            int targetHeight = sourceHeight / 2;
            double scaleX = sourceWidth == 0 ? 1.0 : (double)targetWidth / sourceWidth;
            double scaleY = sourceHeight == 0 ? 1.0 : (double)targetHeight / sourceHeight;

            var frames = new JsonArray();
            for (int i = 0; i < sprites.Count; i++)
            {
                ProcessedSprite sprite = sprites[i];
                var position = positions[i];
                int left = position?.X ?? 0; int top = position?.Y ?? 0;
                var bmp = sprite.Image;
                IntVector2 size = new(bmp.Width, bmp.Height);
                var orig = sprite.OriginalSize;
                double leftScaled = left; double topScaled = top; double rightScaled = left + size.X; double bottomScaled = top + size.Y;

                if (gameThemeConfig.IsHd)
                {
                    leftScaled = RoundHalfUp(leftScaled * scaleX);
                    topScaled = RoundHalfUp(topScaled * scaleY);
                    rightScaled = RoundAwayFromZero(rightScaled * scaleX);
                    bottomScaled = RoundAwayFromZero(bottomScaled * scaleY);
                }

                JsonObject frameValues;
                if (sprite.OldFrameJson == null)
                {
                    var trim = sprite.TrimOffset;
                    AnimationConfig animConfig = subjectConfig.AnimationConfigs[sprite.AnimationName];
                    var recover = animConfig.RecoverCroppedOffset;

                    int trimLeft = trim.X; int trimTop = trim.Y;
                    int originalWidth = orig.X; int originalHeight = orig.Y;
                    if (!recover.X)
                    {
                        trimLeft = 0;
                        originalWidth = (int)Math.Abs(rightScaled - leftScaled);
                        if (gameThemeConfig.IsHd) originalWidth *= 2;
                    }
                    if (!recover.Y)
                    {
                        trimTop = 0;
                        originalHeight = (int)Math.Abs(bottomScaled - topScaled);
                        if (gameThemeConfig.IsHd) originalHeight *= 2;
                    }

                    var extra = animConfig.Offset;
                    double originOffsetX = originalWidth / 2.0 - trimLeft;
                    double originOffsetY = originalHeight - trimTop;
                    if (gameThemeConfig.IsHd)
                    {
                        originOffsetX += extra.Value.X;
                        originOffsetY += extra.Value.Y;
                        originOffsetX = RoundAwayFromZero(originOffsetX * scaleX);
                        originOffsetY = RoundAwayFromZero(originOffsetY * scaleY);
                    }
                    else
                    {
                        originOffsetX += extra.Value.X;
                        originOffsetY += extra.Value.Y;
                        originOffsetX = RoundAwayFromZero(originOffsetX);
                        originOffsetY = RoundAwayFromZero(originOffsetY);
                    }
                    var offsetText = $"{originOffsetX} {originOffsetY}";
                    frameValues = new JsonObject { ["Offset"] = offsetText };
                }
                else
                {


                    frameValues = sprite.OldFrameJson.DeepClone().AsObject();
                }

                frameValues["Rect"] = $"{leftScaled} {topScaled} {rightScaled} {bottomScaled}";
                frames.Add(frameValues);
            }

            var named = new JsonArray();
            foreach (var anim in animations)
            {
                var name = anim["name"].ToString();
                var framesList = (List<int>)anim["frames"];
                var delay = Convert.ToInt32(anim["delay"]);
                var frameStr = string.Join(",", framesList);
                named.Add(new JsonObject { ["Name"] = name, ["Frames"] = frameStr, ["Delay"] = delay });
            }

            var payload = new JsonObject
            {
                ["Frames"] = frames,
                ["NamedAnimations"] = named,
                ["SubPositions"] = subPositions,
                ["Version"] = "Neoarc's Sprite v2.0"
            };
            return payload;
        }

        static Image CreateSpriteSheet(List<ProcessedSprite> sprites, List<IntVector2?> positions, IntVector2 canvasSize)
        {
            if (canvasSize.X <= 1 || canvasSize.Y <= 1) throw new InvalidOperationException("Sprites don't exist.");
            var sheet = Image.Black(canvasSize.X, canvasSize.Y, bands: 4);
            for (int i = 0; i < sprites.Count; i++)
            {
                var pos = positions[i];
                if (pos == default) continue;
                var bmp = sprites[i].Image;
                sheet = sheet.Insert(bmp, pos?.X ?? 0, pos?.Y ?? 0, expand: false, background: [0, 0, 0, 0]);
            }
            return sheet;
        }

        static LayoutInfo SelectLayout(List<ProcessedSprite> sprites)
        {
            if (sprites.Count == 0)
            {
                IntVector2 canvasSize = new(subjectConfig.Sheet.Width ?? 0, subjectConfig.Sheet.Height ?? 0);
                return new LayoutInfo(canvasSize, canvasSize, []);
            }
            if (subjectConfig.Sheet.Width.HasValue)
            {
                var layout = LayoutForWidth(sprites, subjectConfig.Sheet.Width.Value);
                if (subjectConfig.Sheet.Height.HasValue && layout.size.Y > subjectConfig.Sheet.Height.Value) throw new InvalidOperationException("Sprites do not fit within the requested sheet height.");
                IntVector2 canvasSize = new(subjectConfig.Sheet.Width.Value, subjectConfig.Sheet.Height ?? layout.size.Y);
                return new(layout.size, canvasSize, layout.positions);
            }
            var auto = AutoLayout(sprites);
            int canvas_h = subjectConfig.Sheet.Height ?? auto.size.Y;
            return new(auto.size, new(auto.size.X, canvas_h), auto.positions);
        }

        private static (IntVector2 size, List<IntVector2?> positions) LayoutForWidth(List<ProcessedSprite> sprites, int widthLimit)
        {
            int gap = gameThemeConfig.IsHd ? 2 : 1;
            if (sprites.Count == 0) return (new(0, 0), new List<IntVector2?>());
            int maxSpriteWidth = sprites.Max(s => s.Image.Width);
            if (widthLimit < maxSpriteWidth) throw new InvalidOperationException("width_limit is smaller than the widest sprite.");

            var rows = new List<(List<int> indices, int width, int height)>();
            var currentIndices = new List<int>();
            int currentWidth = 0, currentHeight = 0;

            for (int index = 0; index < sprites.Count; index++)
            {
                var bmp = sprites[index].Image;
                int spriteWidth = bmp.Width;
                int spriteHeight = bmp.Height;
                int projectedWidth = currentIndices.Count == 0 ? spriteWidth : currentWidth + gap + spriteWidth;
                if (currentIndices.Count > 0 && projectedWidth > widthLimit)
                {
                    rows.Add((currentIndices.ToList(), currentWidth, currentHeight));
                    currentIndices.Clear();
                    currentWidth = 0; currentHeight = 0; projectedWidth = spriteWidth;
                }
                if (currentIndices.Count > 0) currentWidth += gap;
                currentIndices.Add(index);
                currentWidth += spriteWidth;
                currentHeight = Math.Max(currentHeight, spriteHeight);
            }
            if (currentIndices.Count > 0) rows.Add((currentIndices.ToList(), currentWidth, currentHeight));

            int sheetWidth = rows.Max(r => r.width);
            int sheetHeight = 0;
            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                if (rowIndex > 0)
                {
                    sheetHeight += gap;
                    if (gameThemeConfig.IsHd) sheetHeight = EnsureEvenValue(sheetHeight);
                }
                sheetHeight += rows[rowIndex].height;
            }

            var positions = Enumerable.Repeat<IntVector2?>(null, sprites.Count).ToList();
            int yOffset = 0;
            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                var row = rows[rowIndex];
                if (rowIndex > 0)
                {
                    yOffset += gap;
                    if (gameThemeConfig.IsHd) yOffset = EnsureEvenValue(yOffset);
                }
                int xOffset = 0;
                for (int itemIndex = 0; itemIndex < row.indices.Count; itemIndex++)
                {
                    int spriteIndex = row.indices[itemIndex];
                    var bmp = sprites[spriteIndex].Image;
                    int yPos = yOffset + (row.height - bmp.Height);
                    positions[spriteIndex] = new(xOffset, yPos);
                    xOffset += bmp.Width;
                    if (itemIndex < row.indices.Count - 1)
                    {
                        xOffset += gap;
                        if (gameThemeConfig.IsHd) xOffset = EnsureEvenValue(xOffset);
                    }
                }
                yOffset += row.height;
            }

            return (new IntVector2(sheetWidth, sheetHeight), positions);
        }

        private static (IntVector2 size, List<IntVector2?> positions) AutoLayout(List<ProcessedSprite> sprites)
        {
            int gap = gameThemeConfig.IsHd ? 2 : 1;
            if (sprites.Count == 0) return (new(0, 0), new List<IntVector2?>());
            var widths = sprites.Select(s => s.Image.Width).ToList();
            int maxWidth = widths.Max();
            int totalWidth = widths.Sum();
            var candidateWidths = new HashSet<int>() { maxWidth, totalWidth + gap * (sprites.Count - 1) };
            int prefix = 0;
            for (int i = 0; i < widths.Count; i++)
            {
                prefix += widths[i];
                candidateWidths.Add(Math.Max(maxWidth, prefix + gap * i));
            }

            (IntVector2, List<IntVector2?>) bestLayout = default;
            (double, double, double)? bestScore = null;

            foreach (var widthLimit in candidateWidths.OrderBy(x => x))
            {
                (IntVector2 size, List<IntVector2?> positions) layout;
                try
                {
                    layout = LayoutForWidth(sprites, widthLimit);
                }
                catch
                {
                    continue;
                }
                if (subjectConfig.Sheet.Height.HasValue && layout.size.Y > subjectConfig.Sheet.Height.Value) continue;
                double diff = Math.Abs(layout.size.X - layout.size.Y);
                double area = (double)layout.size.X * Math.Max(layout.size.Y, 1);
                double heightGap = subjectConfig.Sheet.Height.HasValue ? Math.Abs(subjectConfig.Sheet.Height.Value - layout.size.Y) : 0.0;
                var score = (heightGap, diff, area);
                if (bestScore == null || score.CompareTo(bestScore.Value) < 0)
                {
                    bestScore = score;
                    bestLayout = (layout.size, layout.positions);
                }
            }

            if (bestScore == null) throw new InvalidOperationException("Unable to find an automatic layout that satisfies the constraints.");
            return (bestLayout.Item1, bestLayout.Item2);
        }



        static Image RemoveColorWithThreshold(Image src)
        {
            var bytes = src.WriteToMemory();
            int bands = src.Bands;
            if (bands < 4) throw new InvalidOperationException("RGBA image is required for background removal.");

            double thr2 = subjectConfig.ColorTreshold * subjectConfig.ColorTreshold;
            byte tr = parsedBackgroundColor!.Value.r;
            byte tg = parsedBackgroundColor.Value.g;
            byte tb = parsedBackgroundColor.Value.b;
            byte ta = parsedBackgroundColor.Value.a;

            for (int i = 0; i < bytes.Length; i += bands)
            {
                int r = bytes[i + 0];
                int g = bytes[i + 1];
                int b = bytes[i + 2];
                int a = bytes[i + 3];

                int dr = r - tr;
                int dg = g - tg;
                int db = b - tb;
                int da = a - ta;
                long dist2 = (long)dr * dr + (long)dg * dg + (long)db * db + (long)da * da;

                if (dist2 <= thr2)
                {
                    bytes[i + 3] = 0;
                    if (programConfig.ReduceFileSize)
                    {
                        bytes[i + 0] = 0;
                        bytes[i + 1] = 0;
                        bytes[i + 2] = 0;
                    }
                }
            }

            return Image.NewFromMemory(bytes, src.Width, src.Height, bands, src.Format);
        }

        static Image ResizeBitmapNearest(Image source, IntVector2 newSize)
        {
            double sx = (double)newSize.X / Math.Max(1, source.Width);
            double sy = (double)newSize.Y / Math.Max(1, source.Height);
            return source.Resize(sx, vscale: sy, kernel: Enums.Kernel.Nearest);
        }

        static (Image cropped, IntVector2 offset) TrimColor(Image src)
        {
            IntVector2 size = new(src.Width, src.Height);
            var bytes = src.WriteToMemory();
            int bands = src.Bands;

            if (subjectConfig.BackgroundColor == null || subjectConfig.RemoveBackground)
            {
                int left = size.X, top = size.Y, right = 0, bottom = 0;
                bool any = false;
                for (int y = 0; y < size.Y; y++)
                {
                    for (int x = 0; x < size.X; x++)
                    {
                        int idx = (y * size.X + x) * bands;
                        byte a = bytes[idx + 3];
                        if (a != 0)
                        {
                            any = true;
                            if (x < left) left = x;
                            if (x > right) right = x;
                            if (y < top) top = y;
                            if (y > bottom) bottom = y;
                        }
                    }
                }
                if (!any) return (src, new(0, 0));
                right++; bottom++;
                if (gameThemeConfig.IsHd)
                {
                    (left, top, right, bottom) = AlignEvenBox(left, top, right, bottom, size);
                }
                var cropped = CropBitmap(src, left, top, right - left, bottom - top);
                return (cropped, new(left, top));
            }
            else
            {

                double thr2 = subjectConfig.ColorTreshold * subjectConfig.ColorTreshold;
                byte tr = parsedBackgroundColor.Value.r, tg = parsedBackgroundColor.Value.g, tb = parsedBackgroundColor.Value.b, ta = parsedBackgroundColor.Value.a;

                bool[] rowHas = new bool[size.Y];
                bool[] colHas = new bool[size.X];
                bool any = false;

                for (int y = 0; y < size.Y; y++)
                {
                    for (int x = 0; x < size.X; x++)
                    {
                        int idx = (y * size.X + x) * bands;
                        int r = bytes[idx + 0];
                        int g = bytes[idx + 1];
                        int b = bytes[idx + 2];
                        int a = bytes[idx + 3];

                        int dr = r - tr;
                        int dg = g - tg;
                        int db = b - tb;
                        int da = a - ta;
                        long dist2 = (long)dr * dr + (long)dg * dg + (long)db * db + (long)da * da;
                        if (dist2 > thr2)
                        {
                            rowHas[y] = true;
                            colHas[x] = true;
                            any = true;
                        }
                    }
                }

                if (!any) return (src, new(0, 0));

                int top = Array.IndexOf(rowHas, true);
                int bottom = size.Y - Array.IndexOf(rowHas.Reverse().ToArray(), true);
                int left = Array.IndexOf(colHas, true);
                int right = size.X - Array.IndexOf(colHas.Reverse().ToArray(), true);

                if (gameThemeConfig.IsHd)
                {
                    (left, top, right, bottom) = AlignEvenBox(left, top, right, bottom, size);
                }

                if (left == 0 && top == 0 && right == size.X && bottom == size.Y)
                {
                    return (src, new(0, 0));
                }

                var cropped = CropBitmap(src, left, top, right - left, bottom - top);
                return (cropped, new(left, top));
            }
        }

        private static (int left, int top, int right, int bottom) AlignEvenBox(int left, int top, int right, int bottom, IntVector2 size)
        {
            int leftAligned = Math.Max(0, left - (left % 2));
            int topAligned = Math.Max(0, top - (top % 2));
            int rightAligned = Math.Min(size.X, right + (right % 2));
            int bottomAligned = Math.Min(size.Y, bottom + (bottom % 2));

            if ((rightAligned - leftAligned) % 2 == 1)
            {
                if (rightAligned < size.X) rightAligned += 1;
                else if (leftAligned > 0) leftAligned -= 1;
            }
            if ((bottomAligned - topAligned) % 2 == 1)
            {
                if (bottomAligned < size.Y) bottomAligned += 1;
                else if (topAligned > 0) topAligned -= 1;
            }
            return (leftAligned, topAligned, rightAligned, bottomAligned);
        }

        private static Image CropBitmap(Image src, int left, int top, int width, int height)
        {
            return src.ExtractArea(left, top, width, height);
        }

        static Image EnsureEvenDimensions(Image src)
        {
            IntVector2 size = new(src.Width, src.Height);
            IntVector2 newSize = new(size.X + (size.X % 2), size.Y + (size.Y % 2));
            if (newSize.X == size.X && newSize.Y == size.Y) return src;
            return src.Embed(0, 0, newSize.X, newSize.Y, extend: Enums.Extend.Background, background: [0, 0, 0, 0]);
        }

        private static int EnsureEvenValue(int v) => (v % 2 == 0) ? v : v + 1;

        private static int RoundHalfUp(double value) => (int)Math.Floor(value + 0.5);

        private static int RoundAwayFromZero(double value)
        {
            if (value > 0) return (int)Math.Ceiling(value - 1e-9);
            if (value < 0) return (int)Math.Floor(value + 1e-9);
            return 0;
        }
    }
}
