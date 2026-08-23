using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace FramesToMMSpriteResources.DataConfig
{
    public class SubjectInterfaceConfig : InterfaceConfig
    {

        [JsonPropertyName("editor_canvas")]
        public CanvasConfig EditorCanvas = new();

        [JsonPropertyName("preview_canvas")]
        public CanvasConfig PreviewCanvas = new();

        [JsonPropertyName("preview_size")]
        public Vector2? PreviewSize = null;
    }

    public class CanvasConfig
    {
        [JsonPropertyName("pan")]
        public Vector2 Pan { get; set; } = Vector2.Zero;

        [JsonPropertyName("zoom")]
        public float Zoom { get; set; } = 1;

        public CanvasConfig() { }

        public CanvasConfig(Vector2 pan, float zoom)
        {
            Pan = pan;
            Zoom = zoom;
        }
    }
}
