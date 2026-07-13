using FramesToMMSpriteResources.DataConfig;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace FramesToMMSpriteResources
{
    public class SubjectConfig : ParentConfig
    {
        [JsonPropertyName("processing")]
        public ProcessingConfig Processing = new();

        [JsonPropertyName("export")]
        public SubjectExportConfig Export = new();

        [JsonIgnore]
        public Dictionary<string, AnimationConfig>? AnimationConfigs = [];

        public SubjectConfig() {
            InterfaceConfig = new SubjectInterfaceConfig();
        }
    }

    public class ProcessingConfig : ICloneable
    {
        [JsonPropertyName("background_color")]
        public string? BackgroundColor;

        [JsonPropertyName("color_threshold")]
        public int ColorTreshold = 100;

        [JsonPropertyName("remove_background")]
        public bool RemoveBackground = true;

        [JsonPropertyName("resize_to_percent")]
        public float ResizeToPercent = 100;

        [JsonPropertyName("sampling_mode")]
        public int FilterMode = 0;

        [JsonPropertyName("mipmap_mode")]
        public int MipmapMode = 0;

        [JsonPropertyName("crop_left")]
        public bool CropLeft = true;

        [JsonPropertyName("crop_top")]
        public bool CropTop = true;

        [JsonPropertyName("crop_right")]
        public bool CropRight = true;

        [JsonPropertyName("crop_bottom")]
        public bool CropBottom = true;

        public ProcessingConfig() { }

        public ProcessingConfig(string? backgroundColor, int colorTreshold, bool removeBackground, float resizeToPercent, int filterMode, int mipmapMode, bool cropLeft, bool cropTop, bool cropRight, bool cropBottom)
        {
            BackgroundColor = backgroundColor;
            ColorTreshold = colorTreshold;
            RemoveBackground = removeBackground;
            ResizeToPercent = resizeToPercent;
            FilterMode = filterMode;
            MipmapMode = mipmapMode;
            CropLeft = cropLeft;
            CropTop = cropTop;
            CropRight = cropRight;
            CropBottom = cropBottom;
        }

        public object Clone()
        {
            return new ProcessingConfig
            {
                BackgroundColor = this.BackgroundColor,
                ColorTreshold = this.ColorTreshold,
                RemoveBackground = this.RemoveBackground,
                ResizeToPercent = this.ResizeToPercent,
                FilterMode = this.FilterMode,
                MipmapMode = this.MipmapMode,
                CropLeft = this.CropLeft,
                CropTop = this.CropTop,
                CropRight = this.CropRight,
                CropBottom = this.CropBottom
            };
        }
    }
  
    public class SubjectExportConfig
    {
        [JsonPropertyName("width")]
        public int? Width { get; set; }

        [JsonPropertyName("height")]
        public int? Height { get; set; }

        public SubjectExportConfig() { }

        public SubjectExportConfig(int? width, int? height)
        {
            Width = width;
            Height = height;
        }
    }


}
