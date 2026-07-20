using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Globalization;

namespace FramesToMMSpriteResources
{
    public class CustomNumberBox : TextBox
    {
        public static readonly DependencyProperty MinimumProperty =
            DependencyProperty.Register(
                nameof(Minimum),
                typeof(double),
                typeof(CustomNumberBox),
                new PropertyMetadata(double.NegativeInfinity));

        public double Minimum
        {
            get => (double)GetValue(MinimumProperty);
            set => SetValue(MinimumProperty, value);
        }

        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register(
                nameof(Maximum),
                typeof(double),
                typeof(CustomNumberBox),
                new PropertyMetadata(double.PositiveInfinity));

        public double Maximum
        {
            get => (double)GetValue(MaximumProperty);
            set => SetValue(MaximumProperty, value);
        }

        private float? _value;
        public float? Value
        {
            get
            {
                float? valueToReturn = _value;
                if(valueToReturn == null)
                {
                    if (float.TryParse(PlaceholderText, out float parsedDefaultNumber))
                    {
                        valueToReturn = parsedDefaultNumber;
                    }
                }
               
                return valueToReturn;
            }
            set
            {
                float? newValue = value;
                if(newValue != null)
                {
                    newValue = (float)Math.Clamp(newValue.Value, Minimum, Maximum);
                }

                if (_value == newValue) return;

                _value = newValue;
                suppressTextChanged = true;
                Text = _value.ToString();
                SelectionStart = Text?.Length??0;
                SelectionLength = 0;
                suppressTextChanged = false;
                ValueChanged?.Invoke(Value);
            }
        }


        public float Step { get; set; } = 1f;

        private bool isTextValid = true;
        private bool suppressTextChanged = false;

        public event Action<float?>? ValueChanged;

        public CustomNumberBox()
        {
            CornerRadius = new CornerRadius(4);
  

            GotFocus += (_, _) => SelectAll();
            TextChanged += CustomNumberBox_TextChanged;
            LostFocus += CustomNumberBox_LostFocus;
 
            PointerWheelChanged += CustomNumberBox_PointerWheelChanged;
        }

        private void CustomNumberBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            isTextValid = true;
            if (string.IsNullOrWhiteSpace(Text))
            {
                Value = null;
                return;
            }

            if (float.TryParse(Text, out float value))
            {
                float clampedValue = (float)Math.Clamp(value, Minimum, Maximum);
                if (value == clampedValue)
                {
                    Value = value;
                    return;
                } 
                _value = clampedValue;
            }
      
            isTextValid = false;
            
        }

        private void CustomNumberBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!isTextValid)
            {
                suppressTextChanged = true;
                Text = Value?.ToString();
                suppressTextChanged = false;
                isTextValid = true;
            }
        }

        private void CustomNumberBox_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            if (FocusState == FocusState.Unfocused)
                return;

            float? value = Value;
            if (value == null) return;

            var delta = e.GetCurrentPoint(this).Properties.MouseWheelDelta;
            if (delta == 0)
                return;

            var scrolledValue = delta > 0 ? value + Step : value - Step;
            
            Value = (float)Math.Clamp(scrolledValue.Value, Minimum, Maximum);

            e.Handled = true;
        }
    }
}