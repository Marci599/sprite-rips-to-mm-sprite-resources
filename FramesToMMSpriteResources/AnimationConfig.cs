using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace FramesToMMSpriteResources
{
    public enum AlignBasedOn
    {
        RawSpriteSie = 0,
        CroppedSpriteSize = 1
    }
    public class AnimationConfig : ParentConfig
    {
        [JsonPropertyName("regenerate")]
        public bool Regenerate = true;

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

        [JsonPropertyName("align_based_on")]
        public AlignBasedOn AlignBasedOn = AlignBasedOn.RawSpriteSie;

        [JsonPropertyName("direction")]
        public float Direction = 90;

        [JsonPropertyName("speed")]
        public float Speed = 0;

        [JsonPropertyName("preview_frame_range")]
        public RangeConfig Range = new();

        public AnimationConfig() { }

        public AnimationConfig(bool regenerate, int delay, Vector2? offset, RecoverCroppedOffset recoverCroppedOffset)
        {
            Regenerate = regenerate;
            Delay = delay;
            Offset = offset;
            RecoverCroppedOffset = recoverCroppedOffset;
        }
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
