using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace FramesToMMSpriteResource
{
    public abstract class ParentConfig
    {
        [JsonPropertyName("is_expanded")]
        public bool IsExpanded = false;
    }
}
