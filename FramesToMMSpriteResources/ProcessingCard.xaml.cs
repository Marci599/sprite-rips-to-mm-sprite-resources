using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FramesToMMSpriteResources;

public sealed partial class ProcessingCard : UserControl
{
    public ProcessingCard()
    {
        InitializeComponent();

        Func<string, string> hashtag = (s =>
        {
            s = s.Trim();
            if (!s.StartsWith('#'))
            {
                s = '#' + s;
            }
            return s;
        });

        GetColorTextBox.FormatText = hashtag;

        GetColorTextBox.CheckRule = (s =>
        {
          

            if (s.StartsWith('#'))
                s = s[1..];

            return ColorHelper.CanParse(s, out uint _);
        });

    }


    public void ColorTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateColorPreview();
    }

    public CheckBox GetRemoveBackgroundCheckBox { get => RemoveBackgroundCheckBox; }
    public CheckBox GetCropSpritesCheckBox { get => CropSpritesCheckBox; }
    public CheckBox GetCropLeftCheckBox { get => CropLeftCheckBox; }
    public CheckBox GetCropTopCheckBox { get => CropTopCheckBox; }
    public CheckBox GetCropRightCheckBox { get => CropRightCheckBox; }
    public CheckBox GetCropBottomCheckBox { get => CropBottomCheckBox; }
    public CustomNumberBox GetResizeTextBox { get => ResizeTextBox; }
    public ComboBox GetSamplingComboBox { get => SamplingComboBox; }
    public ComboBox GetMipmapComboBox { get => MipmapComboBox; }
    public CustomTextBox GetColorTextBox { get => ColorTextBox; }
    public CustomNumberBox GetThresholdTextBox { get => ThresholdTextBox; }

    public TextBlock GetProcessingColorHelperText { get => ProcessingColorHelperText; }
    public TextBlock GetProcessingCroppingHelperText1 { get => ProcessingCroppingHelperText1; }
    public TextBlock GetProcessingCroppingHelperText2 { get => ProcessingCroppingHelperText2; }

    public void UpdateColorPreview()
    {
        bool valid = TryNormalizeHexToColor(ColorTextBox.Text, out string normalizedHex, out Windows.UI.Color color);
        if (valid)
        {
            ColorPreviewBorder.Background = new SolidColorBrush(color);
            var brush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];
            ColorPreviewBorder.BorderBrush = brush;
        }
        else
        {
            ColorPreviewBorder.Background = new SolidColorBrush();
            var brush = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
            ColorPreviewBorder.BorderBrush = brush;
        }
    }

    private static bool TryNormalizeHexToColor(string? input, out string normalizedHex, out Windows.UI.Color color)
    {
        normalizedHex = string.Empty;
        color = new Windows.UI.Color();

        if (string.IsNullOrWhiteSpace(input))
            return true;

        string s = input.Trim();
        if (s.StartsWith('#'))
            s = s[1..];

        s = s.ToUpperInvariant();

        if (!ColorRegex().IsMatch(s))
            return false;

        if (s.Length == 6)
        {
            if (uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out uint rgb))
            {
                byte r = (byte)((rgb >> 16) & 0xFF);
                byte g = (byte)((rgb >> 8) & 0xFF);
                byte b = (byte)(rgb & 0xFF);
                color = Windows.UI.Color.FromArgb(255, r, g, b);
                normalizedHex = "#" + s;
                return true;
            }
        }
        else if (s.Length == 8)
        {
            if (uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out uint rgba))
            {
                byte r = (byte)((rgba >> 24) & 0xFF);
                byte g = (byte)((rgba >> 16) & 0xFF);
                byte b = (byte)((rgba >> 8) & 0xFF);
                byte a = (byte)(rgba & 0xFF);
                color = Windows.UI.Color.FromArgb(a, r, g, b);
                normalizedHex = "#" + s;
                return true;
            }
        }

        return false;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\A[0-9A-F]+\z")]
    private static partial System.Text.RegularExpressions.Regex ColorRegex();




}

