using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace FramesToMMSpriteResource
{
    public class SubjectConfig : ParentConfig
    {
        [JsonPropertyName("resize_to_percent")]
        public int ResizeToPercent = 100;

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
