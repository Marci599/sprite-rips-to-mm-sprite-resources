using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace FramesToMMSpriteResources
{
    public class ProgramConfig
    {
        [JsonPropertyName("working_path")]
        public string? WorkingPath;

        [JsonPropertyName("reduce_file_size")]
        public bool ReduceFileSize = false;

        [JsonPropertyName("selected_node_path")]
        public List<string>? SelectedNodePath;

        [JsonPropertyName("selected_nodes")]
        public List<string>? SelectedNodes;

        [JsonPropertyName("is_hd")]

        public bool IsHd = true;
        [JsonPropertyName("last_update_check")]
        public DateTime? LastUpdateCheck { get; set; }

        [JsonIgnore]
        public Dictionary<string, GameThemeConfig>? GameThemeConfigs = [];

        public ProgramConfig() { }

        public ProgramConfig(string? workingPath = null, bool reduceFileSize = false, Dictionary<string, GameThemeConfig>? gameThemeConfigs = null, List<string>? selectedNodePath = null, List<string>? selectedNodes = null)
        {
            WorkingPath = workingPath;
            ReduceFileSize = reduceFileSize;
            GameThemeConfigs = gameThemeConfigs;
            SelectedNodePath = selectedNodePath;
            SelectedNodes = selectedNodes;
        }
    }
}
