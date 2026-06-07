using System.Text.Json.Serialization;

namespace FramesToMMSpriteResources
{
    public class FrameConfig
    {
        [JsonPropertyName("offset")]
        public IntVector2 Offset = new IntVector2(0,0);

        [JsonPropertyName("multiply_delay_by")]
        public int MultipyDelayBy = 1;

        [JsonIgnore]
        public string Name;

        public FrameConfig()
        {
  
        }
        public FrameConfig(string name)
        {
            Name = name;
        }



    }
}