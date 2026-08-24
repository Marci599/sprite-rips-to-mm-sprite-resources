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
        [JsonPropertyName("note")]
        public string Note;

        [JsonPropertyName("processing")]
        public ProcessingConfig? Processing;

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
        public string BackgroundColor = "#00FF00";

        [JsonPropertyName("color_threshold")]
        public int ColorThreshold = 100;

        [JsonPropertyName("remove_background")]
        public bool RemoveBackground = true;

        [JsonPropertyName("resize_to_percent")]
        public float ResizeToPercent = 100;

        [JsonPropertyName("sampling_mode")]
        public int FilterMode = 1;

        [JsonPropertyName("mipmap_mode")]
        public int MipmapMode = 1;

        [JsonPropertyName("crop_left")]
        public bool CropLeft = true;

        [JsonPropertyName("crop_top")]
        public bool CropTop = true;

        [JsonPropertyName("crop_right")]
        public bool CropRight = true;

        [JsonPropertyName("crop_bottom")]
        public bool CropBottom = true;

        public ProcessingConfig() { }

        public ProcessingConfig(string backgroundColor, int colorTreshold, bool removeBackground, float resizeToPercent, int filterMode, int mipmapMode, bool cropLeft, bool cropTop, bool cropRight, bool cropBottom)
        {
            BackgroundColor = backgroundColor;
            ColorThreshold = colorTreshold;
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
                ColorThreshold = this.ColorThreshold,
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
