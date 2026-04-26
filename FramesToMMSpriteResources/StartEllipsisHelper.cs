using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI;
using System;

namespace FramesToMMSpriteResources
{

    //DOESN'T WORK WITH TREEVIEW RECYCLING
    public static class StartEllipsisHelper
    {
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.RegisterAttached(
                "Text",
                typeof(string),
                typeof(StartEllipsisHelper),
                new PropertyMetadata(null, OnTextChanged));

        public static void SetText(DependencyObject obj, string value)
            => obj.SetValue(TextProperty, value);

        public static string GetText(DependencyObject obj)
            => (string)obj.GetValue(TextProperty);

        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled",
                typeof(bool),
                typeof(StartEllipsisHelper),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static void SetIsEnabled(DependencyObject obj, bool value)
            => obj.SetValue(IsEnabledProperty, value);

        public static bool GetIsEnabled(DependencyObject obj)
            => (bool)obj.GetValue(IsEnabledProperty);

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBlock tb)
                return;

            tb.Loaded -= TextBlock_Loaded;
            tb.SizeChanged -= TextBlock_SizeChanged;

            if ((bool)e.NewValue)
            {
                tb.Loaded += TextBlock_Loaded;
                tb.SizeChanged += TextBlock_SizeChanged;
            }
        }

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextBlock tb && GetIsEnabled(tb))
            {
                UpdateText(tb);
            }
        }

        private static void TextBlock_Loaded(object sender, RoutedEventArgs e)
            => UpdateText((TextBlock)sender);

        private static void TextBlock_SizeChanged(object sender, SizeChangedEventArgs e)
            => UpdateText((TextBlock)sender);

        private static void UpdateText(TextBlock tb)
        {
            var fullText = GetText(tb) ?? string.Empty;

            if (string.IsNullOrEmpty(fullText))
            {
                tb.Text = string.Empty;
                return;
            }

            // Ha még nincs rendes méret, ne írjuk felül üresre
            if (tb.ActualWidth <= 0)
            {
                tb.Text = fullText;
                return;
            }

            var availableWidth = tb.ActualWidth;
            var ellipsis = "…";

            const double Epsilon = 2; // pixel tolerancia

            if (MeasureTextWidth(tb, fullText) <= availableWidth + Epsilon)
            {
                tb.Text = fullText;
                return;
            }

            int left = 0;
            int right = fullText.Length;

            while (left < right)
            {
                int mid = (left + right) / 2;
                var candidate = ellipsis + fullText.Substring(mid);

                if (MeasureTextWidth(tb, candidate) > availableWidth + Epsilon)
                    left = mid + 1;
                else
                    right = mid;
            }

            tb.Text = ellipsis + fullText.Substring(left);
        }

        private static double MeasureTextWidth(TextBlock source, string text)
        {
            var measureBlock = new TextBlock
            {
                Text = text,
                FontFamily = source.FontFamily,
                FontSize = source.FontSize,
                FontStyle = source.FontStyle,
                FontWeight = source.FontWeight,
                FontStretch = source.FontStretch,
                CharacterSpacing = source.CharacterSpacing,
                TextWrapping = source.TextWrapping
            };

            measureBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return measureBlock.DesiredSize.Width;
        }
    }
}
