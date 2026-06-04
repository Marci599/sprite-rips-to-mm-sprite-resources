using FramesToMMSpriteResources.DataConfig;
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

        [JsonIgnore]
        public Dictionary<string, AnimationConfig>? AnimationConfigs = [];

        public SubjectConfig() {
            InterfaceConfig = new SubjectInterfaceConfig();
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
