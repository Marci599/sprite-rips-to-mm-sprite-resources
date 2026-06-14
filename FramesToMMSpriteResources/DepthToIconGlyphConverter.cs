using Microsoft.UI.Xaml.Data;
using System;

namespace FramesToMMSpriteResources
{
    public class DepthToIconGlyphConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is ItemDepth depth)
            {
                // Set your glyphs for each depth here
                return depth switch
                {
                    ItemDepth.Subject => "\uF158",
                    ItemDepth.Animation => "\uE805",
                    ItemDepth.Frame => "\uE91B",
                    _ => "\uE10C", // Default
                };
            }
            return "\uE10C";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
