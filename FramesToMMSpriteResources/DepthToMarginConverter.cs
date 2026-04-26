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
                    ItemDepth.GameTheme => new Thickness(-249, 0, 0, 0),
                    ItemDepth.Subject => new Thickness(-265, 0, 0, 0),
                    ItemDepth.Animation => new Thickness(-281, 0, 0, 0),
                    ItemDepth.Frame => new Thickness(-290, 0, 0, 0),
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
