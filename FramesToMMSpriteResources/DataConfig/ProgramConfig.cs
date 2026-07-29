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

        [JsonPropertyName("working_path_history")]
        public List<string> WorkingPathHistory = [];

        [JsonPropertyName("reduce_file_size")]
        public bool ReduceFileSize = false;

        [JsonPropertyName("last_update_check")]
        public DateTime? LastUpdateCheck { get; set; }

        [JsonPropertyName("show_previous_frame_behind")]
        public bool ShowPreviousFrameBehind = false;

        [JsonIgnore]
        public AssetConfig? AssetConfig = null;


        [JsonPropertyName("selected_node_path")]
        public List<string>? SelectedNodePath;

        [JsonPropertyName("selected_nodes")]
        public List<string>? SelectedNodes;

        public ProgramConfig() { }
    }
}
