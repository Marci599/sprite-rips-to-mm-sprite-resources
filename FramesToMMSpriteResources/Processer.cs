using Microsoft.UI.Input;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace FramesToMMSpriteResources
{
    internal sealed class ProcessedSprite : IDisposable
    {
        public SKBitmap Image { get; }
        public IntVector2 OriginalSize { get; }
        public IntVector2 TrimOffset { get; }
        public string AnimationName { get; }
        public JsonObject? OldFrameJson { get; }

        public ProcessedSprite(SKBitmap image, IntVector2 originalSize, IntVector2 trimOffset, string animationName, JsonObject? oldFrameJson)
        {
            Image = image;
            OriginalSize = originalSize;
            TrimOffset = trimOffset;
            AnimationName = animationName;
            OldFrameJson = oldFrameJson;
        }

        public void Dispose() => Image.Dispose();
    }

    internal sealed class LayoutInfo
    {
        public IntVector2 LayoutSize { get; }
        public IntVector2 CanvasSize { get; }
        public List<IntVector2?> Positions { get; }

        public LayoutInfo(IntVector2 layoutSize, IntVector2 canvasSize, List<IntVector2?> positions)
        {
            LayoutSize = layoutSize;
            CanvasSize = canvasSize;
            Positions = positions;
        }
    }

    public class PreviousSpriteFileValues
    {
        public Dictionary<string, List<JsonObject>> FramesByAnimation { get; set; } = new();
        public string SubPositions { get; set; } = string.Empty;
    }

    internal static class Processer
    {
        private static ProgramConfig programConfig = null!;
        private static GameThemeConfig gameThemeConfig = null!;
        private static SubjectConfig subjectConfig = null!;
        private static (byte r, byte g, byte b, byte a)? parsedBackgroundColor;

        public static async Task StartProcessAsync(string gameThemeName, string subjectName)
        {
            programConfig = MainWindow.ProgramConfig;
            gameThemeConfig = programConfig.GameThemeConfigs[gameThemeName];
            subjectConfig = gameThemeConfig.SubjectConfigs[subjectName];
            parsedBackgroundColor = null;
            if (subjectConfig.BackgroundColor != null)
            {
                ColorHelper.TryParse(subjectConfig.BackgroundColor, out byte a, out byte r, out byte g, out byte b);
                parsedBackgroundColor = (r, g, b, a);
            }

            string subjectPath = MainWindow.IsUsingGameThemes
                ? Path.Combine(MainWindow.WorkingPath, gameThemeName, subjectName)
                : Path.Combine(MainWindow.WorkingPath, subjectName);

            List<ProcessedSprite> processedSprites = new();
            List<Dictionary<string, object>> animationsMeta = new();
            int frameIndex = 0;

            string subPositions = string.Empty;
            string outputDir = Path.Combine(subjectPath, "generated");
            string spriteFilePath = Path.Combine(outputDir, subjectName + ".sprite");

            JsonArray? frames = null;
            JsonArray? named = null;

            if (File.Exists(spriteFilePath))
            {
                var txt = File.ReadAllText(spriteFilePath);
                var spritePayload = JsonNode.Parse(txt)?.AsObject();
                frames = spritePayload?["Frames"]?.AsArray();
                named = spritePayload?["NamedAnimations"]?.AsArray();
                subPositions = spritePayload?["SubPositions"]?.GetValue<string>() ?? string.Empty;


                var _previousSpriteFile = new PreviousSpriteFileValues
                {
                    FramesByAnimation = new Dictionary<string, List<JsonObject>>(),
                    SubPositions = subPositions
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
                        var indices = framesField.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                                 .Select(s => int.Parse(s.Trim()))
                                                 .ToList();

                        list = new List<JsonObject>();
                        if (frames != null)
                        {
                            foreach (var idx in indices)
                            {
                                if (idx < 0 || idx >= frames.Count) continue;
                                if (frames[idx] is JsonObject frameObj)
                                    list.Add(frameObj);
                            }
                        }
                        break;
                    }
                }
                
                animationConfig.GeneratedFrameCount = -1;
                

                int spritesCount = 0;
                string animationPath = Path.Combine(subjectPath, "raw", animationName);

                var i = 0;

                var spritePaths = Directory.GetFiles(animationPath)
    .Where(p => Path.GetExtension(p) == ".png")
    .ToArray();

                var localSprites = new ProcessedSprite[spritePaths.Length];

                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = Environment.ProcessorCount
                };

                Parallel.For(0, spritePaths.Length, parallelOptions, i =>
                {
                    var spritePath = spritePaths[i];

                    SKBitmap working = SKBitmap.Decode(spritePath)
                        ?? throw new InvalidOperationException($"Failed to decode sprite: {spritePath}");

                    try
                    {
                        if (!string.IsNullOrEmpty(subjectConfig.BackgroundColor) && subjectConfig.RemoveBackground)
                            RemoveColorWithThreshold(working);

                        if (subjectConfig.ResizeToPercent != 100 && subjectConfig.ResizeToPercent > 0)
                        {
                            var scale = subjectConfig.ResizeToPercent / 100.0;
                            int newW = Math.Max(1, (int)(working.Width * scale + 0.5));
                            int newH = Math.Max(1, (int)(working.Height * scale + 0.5));

                            if (newW != working.Width || newH != working.Height)
                            {
                                var resized = ResizeBitmapNearest(working, newW, newH);
                                if (!ReferenceEquals(resized, working))
                                {
                                    working.Dispose();
                                    working = resized;
                                }
                            }
                        }

                        var originalSize = new IntVector2(working.Width, working.Height);

                        SKBitmap imgAfterTrim = working;
                        IntVector2 offset = new(0, 0);

                        if (subjectConfig.CropSprites)
                        {
                            (imgAfterTrim, offset) = TrimColor(working);
                            if (!ReferenceEquals(imgAfterTrim, working))
                            {
                                working.Dispose();
                                working = imgAfterTrim;
                            }
                        }

                        if (gameThemeConfig.IsHd)
                        {
                            var even = EnsureEvenDimensions(working);
                            if (!ReferenceEquals(even, working))
                            {
                                working.Dispose();
                                working = even;
                            }
                        }

                        JsonObject? oldJson = (list != null && i < list.Count) ? list[i] : null;

                        localSprites[i] = new ProcessedSprite(working, originalSize, offset, animationName, oldJson);
                        working = null;
                    }
                    finally
                    {
                        working?.Dispose();
                    }
                });

                processedSprites.AddRange(localSprites);
                spritesCount += localSprites.Length;

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
            using var sheetImage = CreateSpriteSheet(processedSprites, finalPositions, canvasSize);
            var payload = ExportSpriteMetadata(processedSprites, finalPositions, canvasSize, animationsMeta, subPositions);

            if (Directory.Exists(outputDir))
            {
                foreach (var child in Directory.EnumerateFiles(outputDir))
                {
                    try { File.Delete(child); }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex.Message);
                        Debug.WriteLine(ex.StackTrace);
                    }
                }
            }
            Directory.CreateDirectory(outputDir);

            string extension = ".png";

            var spritesheetPath = Path.Combine(outputDir, subjectName + extension);
            var spritesheetPath2x = spritesheetPath;

            if (gameThemeConfig.IsHd)
            {
                int halfW = Math.Max(1, (sheetImage.Width + 1) / 2);
                int halfH = Math.Max(1, (sheetImage.Height + 1) / 2);
                using var sheetHalf = ResizeBitmapNearest(sheetImage, halfW, halfH);
                SaveBitmapToFile(sheetHalf, spritesheetPath);
                spritesheetPath2x = Path.Combine(outputDir, subjectName + "@2x" + extension);
            }

            SaveBitmapToFile(sheetImage, spritesheetPath2x);

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(spriteFilePath, payload.ToJsonString(options));

            foreach (var sprite in processedSprites)
                sprite.Dispose();

            RunQueuedOptimizations();
        }

        private static void SaveBitmapToFile(SKBitmap bmp, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            using var image = SKImage.FromBitmap(bmp);

            using var data = image.Encode(SKEncodedImageFormat.Png, 100);


            if (data == null)
                throw new InvalidOperationException($"Failed to encode image: {path}");

            using var stream = File.OpenWrite(path);
            data.SaveTo(stream);

            if (programConfig.ReduceFileSize)
            {
                QueuePngOptimization(path);
            }
        }

        private static readonly List<string> _pendingOptimizations = new();
        private static readonly object _optLock = new();

        private static void QueuePngOptimization(string filePath)
        {
            lock (_optLock)
                _pendingOptimizations.Add(filePath);
        }

        private static void RunQueuedOptimizations()
        {
            string exePath = Path.Combine(AppContext.BaseDirectory, "Assets", "Tools", "oxipng.exe");
            if (!File.Exists(exePath) || _pendingOptimizations.Count == 0)
                return;

            var filesArg = string.Join(" ", _pendingOptimizations.Select(f => $"\"{f}\""));

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"-o max --strip all {filesArg}",
                CreateNoWindow = true,
                UseShellExecute = false
            };

            using var process = Process.Start(psi);
            process?.WaitForExit();

            _pendingOptimizations.Clear();
        }
        private static JsonObject ExportSpriteMetadata(
            List<ProcessedSprite> sprites,
            List<IntVector2?> positions,
            IntVector2 canvasSize,
            List<Dictionary<string, object>> animations,
            string subPositions)
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
                int left = position?.X ?? 0;
                int top = position?.Y ?? 0;
                var bmp = sprite.Image;
                IntVector2 size = new(bmp.Width, bmp.Height);
                var orig = sprite.OriginalSize;
                double leftScaled = left;
                double topScaled = top;
                double rightScaled = left + size.X;
                double bottomScaled = top + size.Y;

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

                    int trimLeft = trim.X;
                    int trimTop = trim.Y;
                    int originalWidth = orig.X;
                    int originalHeight = orig.Y;

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
                    originOffsetX += extra.Value.X;
                    originOffsetY += extra.Value.Y;

                    if (gameThemeConfig.IsHd)
                    {
                        originOffsetX = RoundAwayFromZero(originOffsetX * scaleX);
                        originOffsetY = RoundAwayFromZero(originOffsetY * scaleY);
                    }
                    else
                    {
                        originOffsetX = RoundAwayFromZero(originOffsetX);
                        originOffsetY = RoundAwayFromZero(originOffsetY);
                    }

                    frameValues = new JsonObject { ["Offset"] = $"{originOffsetX} {originOffsetY}" };
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

            return new JsonObject
            {
                ["Frames"] = frames,
                ["NamedAnimations"] = named,
                ["SubPositions"] = subPositions,
                ["Version"] = "Neoarc's Sprite v2.0"
            };
        }

        private static SKBitmap CreateSpriteSheet(List<ProcessedSprite> sprites, List<IntVector2?> positions, IntVector2 canvasSize)
        {
            if (canvasSize.X <= 1 || canvasSize.Y <= 1)
                throw new InvalidOperationException("Sprites don't exist.");

            var sheet = new SKBitmap(new SKImageInfo(canvasSize.X, canvasSize.Y, SKColorType.Bgra8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(sheet))
            {
                canvas.Clear(SKColors.Transparent);
                for (int i = 0; i < sprites.Count; i++)
                {
                    var pos = positions[i];
                    if (pos == null) continue;
                    var bmp = sprites[i].Image;
                    canvas.DrawBitmap(bmp, pos.Value.X, pos.Value.Y);
                }
            }
            return sheet;
        }

        private static LayoutInfo SelectLayout(List<ProcessedSprite> sprites)
        {
            if (sprites.Count == 0)
            {
                IntVector2 canvasSize = new(subjectConfig.Sheet.Width ?? 0, subjectConfig.Sheet.Height ?? 0);
                return new LayoutInfo(canvasSize, canvasSize, new List<IntVector2?>());
            }

            if (subjectConfig.Sheet.Width.HasValue)
            {
                var layout = LayoutForWidth(sprites, subjectConfig.Sheet.Width.Value);
                if (subjectConfig.Sheet.Height.HasValue && layout.size.Y > subjectConfig.Sheet.Height.Value)
                    throw new InvalidOperationException("Sprites do not fit within the requested sheet height.");

                IntVector2 canvasSize = new(subjectConfig.Sheet.Width.Value, subjectConfig.Sheet.Height ?? layout.size.Y);
                return new LayoutInfo(layout.size, canvasSize, layout.positions);
            }

            var auto = AutoLayout(sprites);
            int canvasH = subjectConfig.Sheet.Height ?? auto.size.Y;
            return new LayoutInfo(auto.size, new IntVector2(auto.size.X, canvasH), auto.positions);
        }

        private static (IntVector2 size, List<IntVector2?> positions) LayoutForWidth(List<ProcessedSprite> sprites, int widthLimit)
        {
            int gap = gameThemeConfig.IsHd ? 2 : 1;
            if (sprites.Count == 0)
                return (new IntVector2(0, 0), new List<IntVector2?>());

            int maxSpriteWidth = sprites.Max(s => s.Image.Width);
            if (widthLimit < maxSpriteWidth)
                throw new InvalidOperationException("width_limit is smaller than the widest sprite.");

            var rows = new List<(List<int> indices, int width, int height)>();
            var currentIndices = new List<int>();
            int currentWidth = 0;
            int currentHeight = 0;

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
                    currentWidth = 0;
                    currentHeight = 0;
                    projectedWidth = spriteWidth;
                }

                if (currentIndices.Count > 0)
                    currentWidth += gap;

                currentIndices.Add(index);
                currentWidth += spriteWidth;
                currentHeight = Math.Max(currentHeight, spriteHeight);
            }

            if (currentIndices.Count > 0)
                rows.Add((currentIndices.ToList(), currentWidth, currentHeight));

            int sheetWidth = rows.Max(r => r.width);
            int sheetHeight = 0;
            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                if (rowIndex > 0)
                {
                    sheetHeight += gap;
                    if (gameThemeConfig.IsHd)
                        sheetHeight = EnsureEvenValue(sheetHeight);
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
                    if (gameThemeConfig.IsHd)
                        yOffset = EnsureEvenValue(yOffset);
                }

                int xOffset = 0;
                for (int itemIndex = 0; itemIndex < row.indices.Count; itemIndex++)
                {
                    int spriteIndex = row.indices[itemIndex];
                    var bmp = sprites[spriteIndex].Image;
                    int yPos = yOffset + (row.height - bmp.Height);
                    positions[spriteIndex] = new IntVector2(xOffset, yPos);
                    xOffset += bmp.Width;
                    if (itemIndex < row.indices.Count - 1)
                    {
                        xOffset += gap;
                        if (gameThemeConfig.IsHd)
                            xOffset = EnsureEvenValue(xOffset);
                    }
                }
                yOffset += row.height;
            }

            return (new IntVector2(sheetWidth, sheetHeight), positions);
        }

        private static (IntVector2 size, List<IntVector2?> positions) AutoLayout(List<ProcessedSprite> sprites)
        {
            int gap = gameThemeConfig.IsHd ? 2 : 1;
            if (sprites.Count == 0)
                return (new IntVector2(0, 0), new List<IntVector2?>());

            var widths = new int[sprites.Count];
            for (int i = 0; i < sprites.Count; i++)
                widths[i] = sprites[i].Image.Width;

            int maxWidth = widths.Max();
            int totalWidth = widths.Sum();
            var candidateWidths = new HashSet<int> { maxWidth, totalWidth + gap * (sprites.Count - 1) };

            int prefix = 0;
            for (int i = 0; i < widths.Length; i++)
            {
                prefix += widths[i];
                candidateWidths.Add(Math.Max(maxWidth, prefix + gap * i));
            }

            (IntVector2 size, List<IntVector2?> positions) bestLayout = default;
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

                if (subjectConfig.Sheet.Height.HasValue && layout.size.Y > subjectConfig.Sheet.Height.Value)
                    continue;

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

            if (bestScore == null)
                throw new InvalidOperationException("Unable to find an automatic layout that satisfies the constraints.");

            return (bestLayout.size, bestLayout.positions);
        }

        private static void RemoveColorWithThreshold(SKBitmap src)
        {
            if (parsedBackgroundColor == null)
                return;

            var (r, g, b, a) = parsedBackgroundColor.Value;
            ColorHelper.RemoveColorWithThresholdInPlace(src, r, g, b, a, subjectConfig.ColorTreshold);
        }

        private static SKBitmap ResizeBitmapNearest(SKBitmap source, int newW, int newH)
        {
            if (newW == source.Width && newH == source.Height)
                return source;

            var sampling = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);
            var resized = source.Resize(new SKImageInfo(newW, newH, source.ColorType, source.AlphaType), sampling);
            if (resized != null)
                return resized;

            var fallback = new SKBitmap(new SKImageInfo(newW, newH, source.ColorType, source.AlphaType));
            using (var canvas = new SKCanvas(fallback))
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawBitmap(source, new SKRect(0, 0, newW, newH));
            }
            return fallback;
        }

        private static (SKBitmap cropped, IntVector2 offset) TrimColor(SKBitmap src)
        {
            var (left, top, right, bottom) = ColorHelper.RectTrimColor(src, subjectConfig, parsedBackgroundColor);



         

            if (gameThemeConfig.IsHd)
                (left, top, right, bottom) = AlignEvenBox(left, top, right, bottom, new(src.Width, src.Height));

            if (left == 0 && top == 0 && right == src.Width && bottom == src.Height)
                return (src, new IntVector2(0, 0));

            var cropped = ColorHelper.CropBitmap(src, left, top, right - left, bottom - top);
            return (cropped, new IntVector2(left, top));
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



        private static SKBitmap EnsureEvenDimensions(SKBitmap src)
        {
            IntVector2 size = new(src.Width, src.Height);
            IntVector2 newSize = new(size.X + (size.X % 2), size.Y + (size.Y % 2));
            if (newSize.X == size.X && newSize.Y == size.Y)
                return src;

            var padded = new SKBitmap(new SKImageInfo(newSize.X, newSize.Y, src.ColorType, src.AlphaType));
            using (var canvas = new SKCanvas(padded))
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawBitmap(src, 0, 0);
            }
            return padded;
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
