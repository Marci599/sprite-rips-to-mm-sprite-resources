using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Globalization;

namespace FramesToMMSpriteResources
{
    public class CustomNumberBox : TextBox
    {
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
                if (_value == value)
                    return;

                _value = value;
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

            if (float.TryParse(Text, out float number))
            {
                Value = number;
                return;
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

            var delta = e.GetCurrentPoint(this).Properties.MouseWheelDelta;
            if (delta == 0)
                return;

            Value = delta > 0 ? Value + Step : Value - Step;
            
            e.Handled = true;
        }
    }
}