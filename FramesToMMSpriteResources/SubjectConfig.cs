using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace FramesToMMSpriteResources
{
    public class SubjectConfig : ParentConfig
    {
        [JsonPropertyName("resize_to_percent")]
        public float ResizeToPercent = 100;

        [JsonPropertyName("sampling_mode")]
        public int FilterMode = 0;

        [JsonPropertyName("mipmap_mode")]
        public int MipmapMode = 0;

        [JsonPropertyName("background_color")]
        public string? BackgroundColor;

        [JsonPropertyName("color_threshold")]
        public int ColorTreshold = 100;

        [JsonPropertyName("remove_background")]
        public bool RemoveBackground = true;

        [JsonPropertyName("crop_sprites")]
        public bool CropSprites = true;

        [JsonPropertyName("sheet")]
        public SheetConfig Sheet = new();

        [JsonPropertyName("editor_canvas")]
        public CanvasConfig EditorCanvas = new();

        [JsonPropertyName("preview_canvas")]
        public CanvasConfig PreviewCanvas = new();



        [JsonPropertyName("preview_size")]
        public Vector2? PreviewSize = null;




        [JsonIgnore]
        public Dictionary<string, AnimationConfig>? AnimationConfigs = [];

        public SubjectConfig() { }

        public SubjectConfig(int resizeToPercent, string? backgroundColor, int colorTreshold, bool removeBackground, bool cropSprites, SheetConfig sheet, Dictionary<string, AnimationConfig>? animationConfigs)
        {
            this.ResizeToPercent = resizeToPercent;
            this.BackgroundColor = backgroundColor;
            this.ColorTreshold = colorTreshold;
            this.RemoveBackground = removeBackground;
            this.CropSprites = cropSprites;
            Sheet = sheet;
            AnimationConfigs = animationConfigs;
        }
    }

    public class CanvasConfig
    {
        [JsonPropertyName("pan")]
        public Vector2 Pan { get; set; } = Vector2.Zero;

        [JsonPropertyName("zoom")]
        public float Zoom { get; set; } = 1;

        public CanvasConfig() { }

        public CanvasConfig(Vector2 pan, float zoom)
        {
            Pan = pan;
            Zoom = zoom;
        }
    }
    public class SheetConfig
    {
        [JsonPropertyName("width")]
        public int? Width { get; set; }

        [JsonPropertyName("height")]
        public int? Height { get; set; }

        public SheetConfig() { }

        public SheetConfig(int? width, int? height)
        {
            Width = width;
            Height = height;
        }
    }


}
