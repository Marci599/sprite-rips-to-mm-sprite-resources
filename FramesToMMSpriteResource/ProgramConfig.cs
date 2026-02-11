using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace FramesToMMSpriteResource
{
    public class ProgramConfig
    {
        [JsonPropertyName("working_path")]
        public string? WorkingPath;

        [JsonPropertyName("reduce_file_size")]
        public bool ReduceFileSize = false;

        [JsonPropertyName("selected_node")]
        public List<string>? SelectedNode;

        [JsonPropertyName("is_hd")]
        public bool IsHd = true;

        [JsonIgnore]
        public Dictionary<string, GameThemeConfig>? GameThemeConfigs = [];

        public ProgramConfig() { }

        public ProgramConfig(string? workingPath = null, bool reduceFileSize = false, Dictionary<string, GameThemeConfig>? gameThemeConfigs = null, List<string>? selectedNode = null)
        {
            WorkingPath = workingPath;
            ReduceFileSize = reduceFileSize;
            GameThemeConfigs = gameThemeConfigs;
            SelectedNode = selectedNode;
        }
    }
}
