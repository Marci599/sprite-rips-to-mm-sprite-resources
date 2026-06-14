using FramesToMMSpriteResources.DataConfig;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace FramesToMMSpriteResources
{
    public class AssetConfig : ParentConfig
    {
        [JsonPropertyName("is_hd")]
        public bool IsHd = true;

        [JsonPropertyName("generate_path")]
        public string? GeneratePath;

        [JsonIgnore]
        public Dictionary<string, SubjectConfig>? SubjectConfigs = [];

        public AssetConfig() { }
    }
}
