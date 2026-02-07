using Microsoft.UI.Xaml.Data;
using System;

namespace FramesToMMSpriteResource
{
    public class BoolToDoubleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool b = false;
            if (value is bool vb) b = vb;
            double trueWidth = 4.0;
            if (parameter is string ps && double.TryParse(ps, out var p)) trueWidth = p;
            return b ? trueWidth : 0.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (value is double d) return d > 0.5;
            return false;
        }
    }
}
