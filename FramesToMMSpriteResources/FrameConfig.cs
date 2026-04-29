using System.Text.Json.Serialization;

namespace FramesToMMSpriteResources
{
    public class FrameConfig
    {
        [JsonPropertyName("offset")]
        public IntVector2 Offset = new IntVector2(0,0);

        [JsonIgnore]
        public string Name;

        public FrameConfig()
        {
  
        }
        public FrameConfig(string name)
        {
            Name = name;
        }

        public FrameConfig(string name, IntVector2 offset)
        {
            Name = name;
            Offset = offset;
            
        }


    }
}