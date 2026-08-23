using FramesToMMSpriteResources.DataConfig;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace FramesToMMSpriteResources
{

    public class AnimationConfig : ParentConfig
    {
        [JsonPropertyName("regenerate")]
        public bool Regenerate = true;

  

        [JsonPropertyName("delay")]
        public int Delay = 1;

        [JsonPropertyName("loop_type")]
        public int LoopType = 0;

        [JsonPropertyName("skip")]
        public int Skip = 0;

        [JsonPropertyName("offset")]
        public Vector2? Offset;

        [JsonPropertyName("recover_cropped_offset")]
        public RecoverCroppedOffset RecoverCroppedOffset = new();

        [JsonPropertyName("frame_configs")]
        public List<FrameConfig> FrameCongfigs;

        [JsonPropertyName("also_known_as")]
        public Dictionary<string, RangeConfig> AlsoKnownAs = new();

        [JsonPropertyName("processing_overwrite")]
        public ProcessingConfig? ProcessingOverwrite = null;

        public AnimationConfig() { }

        public AnimationInterfaceConfig GetInterfaceConfig()
        {
            return (InterfaceConfig as AnimationInterfaceConfig)!;
    }
    }

    public class RecoverCroppedOffset
    {
        public bool X = true;
        public bool Y = true;

        public RecoverCroppedOffset() { }
        public RecoverCroppedOffset(bool x, bool y)
        {
            this.X = x;
            this.Y = y;
        }
    }
}
