using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace FramesToMMSpriteResources.DataConfig
{
    public class AssetInterfaceConfig
    {
        [JsonPropertyName("generate_path")]
        public string? GeneratePath;

        public AssetInterfaceConfig() { }
    }
}
