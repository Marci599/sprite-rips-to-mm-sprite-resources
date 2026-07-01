using FramesToMMSpriteResources.DataConfig;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace FramesToMMSpriteResources
{
    public class AssetConfig
    {
        [JsonPropertyName("is_hd")]
        public bool IsHd = true;

        [JsonIgnore]
        public Dictionary<string, SubjectConfig>? SubjectConfigs = [];

        [JsonIgnore]
        public AssetInterfaceConfig InterfaceConfig = new();
        public AssetConfig() { }
    }
}
