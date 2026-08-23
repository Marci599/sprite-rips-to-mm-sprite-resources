using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace FramesToMMSpriteResources
{
    public class InterfaceConfig
    {
        [JsonPropertyName("is_expanded")]
        public bool IsExpanded = false;

        public InterfaceConfig() { }
        public InterfaceConfig(bool isExpanded)
        {
            IsExpanded = isExpanded;
        }
    }
}
