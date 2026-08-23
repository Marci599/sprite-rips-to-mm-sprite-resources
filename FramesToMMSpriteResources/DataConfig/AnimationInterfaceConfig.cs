using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace FramesToMMSpriteResources.DataConfig
{
    public enum AlignBasedOn
    {
        RawSpriteSie = 0,
        CroppedSpriteSize = 1
    }



    public class AnimationInterfaceConfig : InterfaceConfig
    {
        [JsonPropertyName("align_on_x_axis")]
        public bool AlignOnXAxis = true;
        [JsonPropertyName("align_on_y_axis")]
        public bool AlignOnYAxis = true;

        [JsonPropertyName("direction")]
        public float Direction = 90;

        [JsonPropertyName("speed")]
        public float Speed = 0;

        [JsonPropertyName("preview_frame_range")]
        public RangeConfig Range = new();

        [JsonPropertyName("align_based_on")]
        public AlignBasedOn AlignBasedOn = AlignBasedOn.RawSpriteSie;

        [JsonPropertyName("also_known_as")]
        public string AlsoKnownAs = "";

        [JsonPropertyName("generated_frame_count")]
        public int GeneratedFrameCount = -1;
        public AnimationInterfaceConfig() { }
    }

    public class RangeConfig
    {
        [JsonPropertyName("from")]
        public int From { get; set; } = 0;

        [JsonPropertyName("to")]
        public int To { get; set; } = -1;

        public RangeConfig() { }

        public RangeConfig(int from, int to)
        {
            From = from;
            From = to;
        }
    }
}
