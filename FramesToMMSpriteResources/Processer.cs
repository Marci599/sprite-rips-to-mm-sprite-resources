using FramesToMMSpriteResources.DataConfig;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace FramesToMMSpriteResources
{
    internal sealed class ProcessedSprite : IDisposable
    {
        public SKBitmap Image { get; }
        public IntVector2 OriginalSize { get; }
        public IntVector2 TrimOffset { get; }
        public IntVector2 FrameOffset { get; }
        public int MultiplyDelayBy { get; }
        public string AnimationName { get; }
        public JsonObject? OldFrameJson { get; }
 

        public ProcessedSprite(SKBitmap image, IntVector2 originalSize, IntVector2 trimOffset, IntVector2 frameOffset, int multiplyDelayBy, string animationName, JsonObject? oldFrameJson)
        {
            Image = image;
            OriginalSize = originalSize;
            TrimOffset = trimOffset;
            FrameOffset = frameOffset;
            MultiplyDelayBy = multiplyDelayBy;
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
        private static SubjectConfig subjectConfig = null!;
        private static (byte r, byte g, byte b, byte a)? parsedBackgroundColor;


        public static async Task StartProcessAsync(string subjectName)
        {


            programConfig = MainWindow.ProgramConfig;
            subjectConfig = programConfig.AssetConfig!.SubjectConfigs[subjectName];
            parsedBackgroundColor = null;
            if (subjectConfig.Processing.BackgroundColor != null)
            {
                ColorHelper.TryParse(subjectConfig.Processing.BackgroundColor, out byte a, out byte r, out byte g, out byte b);
                parsedBackgroundColor = (r, g, b, a);
            }


            List<ProcessedSprite> processedSprites = new();
            List<Dictionary<string, object>> animationsMeta = new();
            int frameIndex = 0;

            string subPositions = string.Empty;
            string outputDir;

   

            if (!Path.Exists(programConfig.AssetConfig!.InterfaceConfig.GeneratePath))
            {
                outputDir = Path.Combine(MainWindow.WorkingPath, "_generated");
            }
            else
            {
                outputDir = programConfig.AssetConfig!.InterfaceConfig.GeneratePath;
   
            }

          

            

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

            foreach (var (animationName, animationConfig) in subjectConfig.AnimationConfigs!)
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
                
                //animationConfig.GeneratedFrameCount = -1;
                

                int spritesCount = 0;
                int spritesCountMultiplied = 0;
                string animationPath = Path.Combine(MainWindow.WorkingPath, subjectName, animationName);

                var spritePaths = GetOrderedSpritePaths(animationPath, animationConfig);

                var localSprites = new ProcessedSprite[spritePaths.Length];

                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = Environment.ProcessorCount
                };

                if (animationConfig.FrameCongfigs.Count > spritePaths.Length)
                {
                    animationConfig.FrameCongfigs.RemoveRange(
                        spritePaths.Length,
                        animationConfig.FrameCongfigs.Count - spritePaths.Length);
                }

                Parallel.For(0, spritePaths.Length, parallelOptions, i =>
                {
                    var spritePath = spritePaths[i];
                    FrameConfig frameConfig = animationConfig.FrameCongfigs[i];
                    IntVector2 frameOffset = frameConfig.Offset;
                    spritesCountMultiplied += frameConfig.MultipyDelayBy;

                    SKBitmap working = SKBitmap.Decode(spritePath)
                        ?? throw new InvalidOperationException($"Failed to decode sprite: {spritePath}");

                    var originalSize = new IntVector2(working.Width, working.Height);
                    IntVector2 offset = new(0, 0);
                    try
                    {
                        ProcessingConfig processingConfig;
                        (byte r, byte g, byte b, byte a)? currentParsedColor = null;
                        if (animationConfig.ProcessingOverwrite != null)
                        {
                            processingConfig = animationConfig.ProcessingOverwrite;
                            ColorHelper.TryParse(animationConfig.ProcessingOverwrite.BackgroundColor, out byte a, out byte r, out byte g, out byte b);
                            currentParsedColor = (r, g, b, a);
                        }
                        else
                        {
                            processingConfig = subjectConfig.Processing;
                            currentParsedColor = parsedBackgroundColor;
                        }
                        
                        if (!string.IsNullOrEmpty(processingConfig.BackgroundColor) && processingConfig.RemoveBackground && currentParsedColor != null)
                                RemoveColorWithThreshold(working, processingConfig, currentParsedColor.Value);

                        if (processingConfig.ResizeToPercent != 100 && processingConfig.ResizeToPercent > 0)
                        {
                            var scale = processingConfig.ResizeToPercent / 100.0;
                            var resized = ResizeBitmapFromOrigin(working, frameOffset, scale, out IntVector2 resizedFrameOffset, new SKSamplingOptions((SKFilterMode)processingConfig.FilterMode, (SKMipmapMode)processingConfig.MipmapMode));
                            if (!ReferenceEquals(resized, working))
                            {
                                working.Dispose();
                                working = resized;
                            }
                            frameOffset = resizedFrameOffset;
                            originalSize = new IntVector2(working.Width, working.Height);
                        }

                      

                        SKBitmap imgAfterTrim = working;
                            

                        if (processingConfig.CropLeft || processingConfig.CropTop || processingConfig.CropRight || processingConfig.CropBottom)
                        {
                            (imgAfterTrim, offset) = TrimColor(working, processingConfig, currentParsedColor);
                            if (!ReferenceEquals(imgAfterTrim, working))
                            {
                                working.Dispose();
                                working = imgAfterTrim;
                            }
                        }
                        

                        

                        if (programConfig.AssetConfig!.IsHd)
                        {
                            var even = EnsureEvenDimensions(working);
                            if (!ReferenceEquals(even, working))
                            {
                                working.Dispose();
                                working = even;
                            }
                        }

                        JsonObject? oldJson = (list != null && i < list.Count) ? list[i] : null;

                        localSprites[i] = new ProcessedSprite(working, originalSize, offset, frameOffset, frameConfig.MultipyDelayBy, animationName, oldJson);
                        working = null;
                    }
                    finally
                    {
                        working?.Dispose();
                    }
                });

                processedSprites.AddRange(localSprites);
                spritesCount += localSprites.Length;

                var frameRange = Enumerable.Range(frameIndex, spritesCountMultiplied).ToList();
                animationsMeta.Add(new Dictionary<string, object>
                {
                    ["name"] = animationName,
                    ["frames"] = frameRange,
                    ["delay"] = animationConfig.Delay,
                    ["loopType"] = animationConfig.LoopType
                });
                if(animationConfig.AlsoKnownAs != null)
                {
                    foreach ((string knownName, RangeConfig knownRange) in animationConfig.AlsoKnownAs)
                    {
                        int firstFrame = frameIndex + knownRange.From;
                        int lastFrame = spritesCountMultiplied;
                        if(knownRange.To != -1)
                        {
                            lastFrame = firstFrame + 1 + knownRange.To;
                        }
                        var knownFrameRange = Enumerable.Range(firstFrame, lastFrame).ToList();
                        animationsMeta.Add(new Dictionary<string, object>
                        {
                            ["name"] = knownName,
                            ["frames"] = knownFrameRange,
                            ["delay"] = animationConfig.Delay,
                            ["loopType"] = animationConfig.LoopType
                        });
                    }
                }
                frameIndex += spritesCountMultiplied;
            }

            void AddWaterMarkPath(string path)
            {
            

                var watermark = SKBitmap.Decode(path)
                    ?? throw new InvalidOperationException("Failed to load watermark.");

                AddWaterMark(watermark);
            }

            void AddWaterMarkAssembly(string path)
            {
                using Stream stream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream("FramesToMMSpriteResources.Assets.WaterMark.png")
                    ?? throw new InvalidOperationException("Watermark resource not found.");

                using var memoryStream = new MemoryStream();
                stream.CopyTo(memoryStream);

                memoryStream.Position = 0;

                var watermark = SKBitmap.Decode(memoryStream)
                    ?? throw new InvalidOperationException("Failed to load watermark.");

                AddWaterMark(watermark);
            }

            void AddWaterMark(SKBitmap sKBitMap)
            {
                processedSprites.Add(new ProcessedSprite(
                    sKBitMap,
                    new IntVector2(sKBitMap.Width, sKBitMap.Height),
                    new IntVector2(0, 0),
                    new IntVector2(0, 0),
                    0,
                    string.Empty,
                    null));
            }



            if (processedSprites.Count > 20)
            {
                if (programConfig.AssetConfig.Note != "Marci599 is cool")
                {
                    AddWaterMarkAssembly("FramesToMMSpriteResources.Assets.WaterMark.png");
                }

                string path = Path.Combine(MainWindow.GetUserConfigDirectory(), "WaterMark.png");
                if (File.Exists(path))
                {
                    AddWaterMarkPath(path);
                }

            }

            var layoutInfo = SelectLayout(processedSprites);
            var finalPositions = layoutInfo.Positions;
            if (finalPositions.Any(p => p is null))
                throw new InvalidOperationException("Failed to generate positions for every sprite.");

            var canvasSize = new IntVector2(layoutInfo.CanvasSize.X, layoutInfo.CanvasSize.Y);
            using var sheetImage = CreateSpriteSheet(processedSprites, finalPositions, canvasSize);
            var payload = ExportSpriteMetadata(processedSprites, finalPositions, canvasSize, animationsMeta, subPositions);

 
            Directory.CreateDirectory(outputDir);

            string extension = ".png";

            var spritesheetPath = Path.Combine(outputDir, subjectName + extension);
            var spritesheetPath2x = spritesheetPath;

            if (programConfig.AssetConfig!.IsHd)
            {
                int halfW = Math.Max(1, (sheetImage.Width + 1) / 2);
                int halfH = Math.Max(1, (sheetImage.Height + 1) / 2);
                using var sheetHalf = ResizeBitmap(sheetImage, halfW, halfH, new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));
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

                if (sprite.MultiplyDelayBy == 0)
                    continue;

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

                if (programConfig.AssetConfig!.IsHd)
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
                    int visibleWidth = (int)Math.Abs(rightScaled - leftScaled);
                    int visibleHeight = (int)Math.Abs(bottomScaled - topScaled);
                    if (programConfig.AssetConfig!.IsHd)
                    {
                        visibleWidth *= 2;
                        visibleHeight *= 2;
                    }

                    double originOffsetX;
                    if (recover.X)
                    {
                        originOffsetX = -sprite.FrameOffset.X - trimLeft;
                    }
                    else
                    {
                        double defaultFrameOffsetX = -(originalWidth / 2.0);
                        double frameOffsetDeltaX = sprite.FrameOffset.X - defaultFrameOffsetX;
                        originOffsetX = visibleWidth / 2.0 - frameOffsetDeltaX;
                    }

                    double originOffsetY;
                    if (recover.Y)
                    {
                        originOffsetY = sprite.FrameOffset.Y - trimTop;
                    }
                    else
                    {
                        double defaultFrameOffsetY = originalHeight;
                        double frameOffsetDeltaY = sprite.FrameOffset.Y - defaultFrameOffsetY;
                        originOffsetY = visibleHeight - frameOffsetDeltaY;
                    }

                    var extra = animConfig.Offset;
                    originOffsetX += extra?.X ?? 0;
                    originOffsetY += extra?.Y ?? 0;

                    if (programConfig.AssetConfig!.IsHd)
                    {
                        originOffsetX = ScaleHdOffset(originOffsetX);
                        originOffsetY = ScaleHdOffset(originOffsetY);
                    }
                    else
                    {
                        originOffsetX = RoundAwayFromZero(originOffsetX);
                        originOffsetY = RoundAwayFromZero(originOffsetY);
                    }

                    frameValues = new JsonObject { ["Offset"] = $"{originOffsetX} {originOffsetY}" };
                    frameValues["Rect"] = $"{leftScaled} {topScaled} {rightScaled} {bottomScaled}";
                    for (int j = 0; j < sprite.MultiplyDelayBy; j++)
                        frames.Add(frameValues.DeepClone());
                }
                else
                {
                    frameValues = sprite.OldFrameJson.DeepClone().AsObject();
                    frameValues["Rect"] = $"{leftScaled} {topScaled} {rightScaled} {bottomScaled}";
                    frames.Add(frameValues);
                }

                
            }

            var named = new JsonArray();

            foreach (var anim in animations)
            {
                var name = anim["name"].ToString();
                var framesList = (List<int>)anim["frames"];
                var delay = Convert.ToInt32(anim["delay"]);
                var loopType = Convert.ToInt32(anim["loopType"]);
                var frameStr = string.Join(",", framesList);
                named.Add(new JsonObject { ["Name"] = name, ["Frames"] = frameStr, ["Delay"] = delay, ["LoopType"] = loopType });
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
                IntVector2 canvasSize = new(subjectConfig.Export.Width ?? 0, subjectConfig.Export.Height ?? 0);
                return new LayoutInfo(canvasSize, canvasSize, new List<IntVector2?>());
            }

            if (subjectConfig.Export.Width.HasValue)
            {
                var layout = LayoutForWidth(sprites, subjectConfig.Export.Width.Value);
                if (subjectConfig.Export.Height.HasValue && layout.size.Y > subjectConfig.Export.Height.Value)
                    throw new InvalidOperationException("Sprites do not fit within the requested sheet height.");

                IntVector2 canvasSize = new(subjectConfig.Export.Width.Value, subjectConfig.Export.Height ?? layout.size.Y);
                return new LayoutInfo(layout.size, canvasSize, layout.positions);
            }

            var auto = AutoLayout(sprites);
            int canvasH = subjectConfig.Export.Height ?? auto.size.Y;
            return new LayoutInfo(auto.size, new IntVector2(auto.size.X, canvasH), auto.positions);
        }

        private static (IntVector2 size, List<IntVector2?> positions) LayoutForWidth(List<ProcessedSprite> sprites, int widthLimit)
        {
            int gap = programConfig.AssetConfig!.IsHd ? 2 : 1;
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
                    if (programConfig.AssetConfig!.IsHd)
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
                    if (programConfig.AssetConfig!.IsHd)
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
                        if (programConfig.AssetConfig!.IsHd)
                            xOffset = EnsureEvenValue(xOffset);
                    }
                }
                yOffset += row.height;
            }

            return (new IntVector2(sheetWidth, sheetHeight), positions);
        }

        private static (IntVector2 size, List<IntVector2?> positions) AutoLayout(List<ProcessedSprite> sprites)
        {
            int gap = programConfig.AssetConfig!.IsHd ? 2 : 1;
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

                if (subjectConfig.Export.Height.HasValue && layout.size.Y > subjectConfig.Export.Height.Value)
                    continue;

                double diff = Math.Abs(layout.size.X - layout.size.Y);
                double area = (double)layout.size.X * Math.Max(layout.size.Y, 1);
                double heightGap = subjectConfig.Export.Height.HasValue ? Math.Abs(subjectConfig.Export.Height.Value - layout.size.Y) : 0.0;
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

        private static void RemoveColorWithThreshold(SKBitmap src, ProcessingConfig processingConfig, (byte r, byte g, byte b, byte a) parsedBackgroundColor)
        {
          
            var (r, g, b, a) = parsedBackgroundColor;
            bool nearest = (processingConfig.FilterMode == 0 && processingConfig.MipmapMode == 0);
            ColorHelper.RemoveColorWithThresholdInPlace(src, r, g, b, a, processingConfig.ColorTreshold, !nearest);
        }



        private static string[] GetOrderedSpritePaths(string animationPath, AnimationConfig animationConfig)
        {
            string[] paths;

            if (animationConfig.FrameCongfigs != null && animationConfig.FrameCongfigs.Count > 0)
            {
                var orderedPaths = animationConfig.FrameCongfigs
                    .Where(frameConfig => !string.IsNullOrWhiteSpace(frameConfig.Name))
                    .Select(frameConfig => Path.Combine(animationPath, $"{frameConfig.Name}.png"))
                    .Where(File.Exists)
                    .ToArray();

                if (orderedPaths.Length > 0)
                    paths = orderedPaths;
                else
                    paths = Directory.GetFiles(animationPath)
                        .Where(p => string.Equals(Path.GetExtension(p), ".png", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
            }
            else
            {
                paths = Directory.GetFiles(animationPath)
                    .Where(p => string.Equals(Path.GetExtension(p), ".png", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }

            // Apply step filter: if Skip = 1, take every file (0,1,2...); if Skip = 2, take every 2nd file (0,2,4...); if Skip = 3, take every 3rd (0,3,6...)
            if (animationConfig.Skip > 1)
            {
                paths = paths
                    .Where((_, index) => index % animationConfig.Skip == 0)
                    .ToArray();
            }

            return paths;
        }



        private static SKBitmap ResizeBitmap(SKBitmap source, int newW, int newH, SKSamplingOptions samplingOptions)
        {
            if (newW == source.Width && newH == source.Height)
                return source;

          
            var resized = source.Resize(new SKImageInfo(newW, newH, source.ColorType, source.AlphaType), samplingOptions);
            if (resized != null)
                return resized;

            var fallback = new SKBitmap(new SKImageInfo(newW, newH, source.ColorType, source.AlphaType));
            using (var canvas = new SKCanvas(fallback))
            using (var image = SKImage.FromBitmap(source))
            using (var paint = new SKPaint { IsAntialias = false })
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawImage(image, new SKRect(0, 0, newW, newH), samplingOptions, paint);
            }
            return fallback;
        }


        private static SKBitmap ResizeBitmapFromOrigin(SKBitmap source, IntVector2 frameOffset, double scale, out IntVector2 resizedFrameOffset, SKSamplingOptions samplingOption)
        {
            double left = frameOffset.X;
            double top = -frameOffset.Y;
            double right = left + source.Width;
            double bottom = top + source.Height;

            int scaledLeft = RoundHalfUp(left * scale);
            int scaledTop = RoundHalfUp(top * scale);
            int scaledRight = RoundHalfUp(right * scale);
            int scaledBottom = RoundHalfUp(bottom * scale);

            int newW = Math.Max(1, scaledRight - scaledLeft);
            int newH = Math.Max(1, scaledBottom - scaledTop);
            resizedFrameOffset = new IntVector2(scaledLeft, -scaledTop);

            if (scale == 1.0 && newW == source.Width && newH == source.Height && resizedFrameOffset == frameOffset)
                return source;

            var resized = new SKBitmap(new SKImageInfo(newW, newH, source.ColorType, source.AlphaType));
            using (var canvas = new SKCanvas(resized))
            using (var image = SKImage.FromBitmap(source))
            using (var paint = new SKPaint { IsAntialias = false })
            {
                canvas.Clear(SKColors.Transparent);
                var destRect = new SKRect(
                    (float)(left * scale - scaledLeft),
                    (float)(top * scale - scaledTop),
                    (float)(right * scale - scaledLeft),
                    (float)(bottom * scale - scaledTop));
                canvas.DrawImage(image, destRect, samplingOption, paint);
            }
            return resized;
        }

        private static (SKBitmap cropped, IntVector2 offset) TrimColor(SKBitmap src, ProcessingConfig processingConfig, (byte r, byte g, byte b, byte a)? parsedBackgroundColor)
        {
  
            var (left, top, right, bottom) = ColorHelper.RectTrimColor(src, subjectConfig, parsedBackgroundColor, processingConfig);

   
            if (!processingConfig.CropLeft) left = 0;
            if (!processingConfig.CropTop) top = 0;
            if (!processingConfig.CropRight) right = src.Width;
            if (!processingConfig.CropBottom) bottom = src.Height;

            if (programConfig.AssetConfig!.IsHd)
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


        private static double ScaleHdOffset(double hdPixelOffset)
            => RoundAwayFromZero(hdPixelOffset) / 2.0;

        private static int RoundHalfUp(double value) => (int)Math.Floor(value + 0.5);

        private static int RoundAwayFromZero(double value)
        {
            if (value > 0) return (int)Math.Ceiling(value - 1e-9);
            if (value < 0) return (int)Math.Floor(value + 1e-9);
            return 0;
        }
    }
}
