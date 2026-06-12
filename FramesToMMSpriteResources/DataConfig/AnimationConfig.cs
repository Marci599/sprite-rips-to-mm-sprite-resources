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

        [JsonPropertyName("exclude")]
        public bool Exclude = false;

        [JsonPropertyName("delay")]
        public int Delay = 1;

        [JsonPropertyName("loop_type")]
        public int LoopType = 0;

        [JsonPropertyName("offset")]
        public Vector2? Offset;

        [JsonPropertyName("recover_cropped_offset")]
        public RecoverCroppedOffset RecoverCroppedOffset = new();

        [JsonPropertyName("generated_frame_count")]
        public int GeneratedFrameCount = -1;

        [JsonPropertyName("frame_configs")]
        public List<FrameConfig> FrameCongfigs;

        [JsonPropertyName("also_known_as")]
        public SortedSet<string> AlsoKnownAs = [];

        public AnimationConfig() { }
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
