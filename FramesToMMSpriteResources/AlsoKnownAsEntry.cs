using FramesToMMSpriteResources.DataConfig;

namespace FramesToMMSpriteResources
{
    public class AlsoKnownAsEntry
    {
        public string Name { get; set; }
        public RangeConfig Range { get; set; }

        public AlsoKnownAsEntry(string name, RangeConfig range)
        {
            Name = name;
            Range = range;
        }
    }
}