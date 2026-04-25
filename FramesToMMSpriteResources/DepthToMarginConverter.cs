using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml;
using System;

namespace FramesToMMSpriteResources
{
    public class DepthToMarginConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is ItemDepth depth)
            {
                // Adjust margins as needed for each depth
                return depth switch
                {
                    ItemDepth.GameTheme => new Thickness(-244, 0, 0, 0),
                    ItemDepth.Subject => new Thickness(-260, 0, 0, 0),
                    ItemDepth.Animation => new Thickness(-276, 0, 0, 0),
                    _ => new Thickness(-250, 0, 0, 0),
                };
            }
            return new Thickness(-250, 0, 0, 0);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
