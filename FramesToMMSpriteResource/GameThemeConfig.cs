using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace FramesToMMSpriteResource
{
    public class GameThemeConfig : ParentConfig
    {
        [JsonPropertyName("is_hd")]
        public bool IsHd = true;

        [JsonIgnore]
        public Dictionary<string, SubjectConfig>? SubjectConfigs = [];

        public GameThemeConfig() { }

        public GameThemeConfig(bool isHd, bool isExpanded)
        {
            this.IsHd = isHd;
            this.IsExpanded = isExpanded;
        }
    }
}
