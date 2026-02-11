using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace FramesToMMSpriteResource
{
    public class AnimationConfig
    {
        [JsonPropertyName("regenerate")]
        public bool Regenerate = true;

        [JsonPropertyName("delay")]
        public int Delay = 1;

        [JsonPropertyName("offset")]
        public Vector2? Offset;

        [JsonPropertyName("recover_cropped_offset")]
        public RecoverCroppedOffset RecoverCroppedOffset = new();

        public AnimationConfig() { }

        public AnimationConfig(bool regenerate, int delay, Vector2? offset, RecoverCroppedOffset recoverCroppedOffset)
        {
            Regenerate = regenerate;
            Delay = delay;
            Offset = offset;
            RecoverCroppedOffset = recoverCroppedOffset;
        }
    }

    public class RecoverCroppedOffset
    {
        public bool x = true;
        public bool y = true;

        public RecoverCroppedOffset() { }
        public RecoverCroppedOffset(bool x, bool y)
        {
            this.x = x;
            this.y = y;
        }
    }
}
