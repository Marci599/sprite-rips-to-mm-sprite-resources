using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace FramesToMMSpriteResources
{
    public class CustomTextBox : TextBox
    {
        public Func<string, bool> CheckRule = (s => true);
        public Func<string, string> FormatText = (s => s.Trim());

        private string _value = "";

        string GetDefaultIfEmpty(string valueToReturn)
        {
            if (string.IsNullOrWhiteSpace(valueToReturn) && !string.IsNullOrWhiteSpace(PlaceholderText))
            {
                return PlaceholderText;
            }
            return valueToReturn;
        }
        public string Value
        {
            get
            {
                return GetDefaultIfEmpty(_value);
            }
            set
            {
                string newValue = value??"";
                if (newValue != "")
                {
                    newValue = FormatText(newValue);
                }

                if (_value == newValue) return;

                _value = newValue;
                suppressTextChanged = true;
                Text = _value;
                SelectionStart = Text?.Length??0;
                SelectionLength = 0;
                suppressTextChanged = false;
                ValueChanged?.Invoke(this, Value);
            }
        }

        private bool isTextValid = true;
        private bool suppressTextChanged = false;

        public event Action<object, string>? ValueChanged;

        public CustomTextBox()
        {
            CornerRadius = new CornerRadius(4);
  
            TextChanged += CustomTextBox_TextChanged;
            LostFocus += CustomTextBox_LostFocus;
        }

        private void CustomTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (suppressTextChanged)
                return;

            isTextValid = true;
            if (string.IsNullOrWhiteSpace(Text) && PlaceholderText != "")
            {
                Value = "";
                if (Text.Length > 0)
                {
                    isTextValid = false;
                }
        
                return;
            }

            if (CheckRule(Text))
            {
                string text = Text;
                string formattedValue = FormatText(text);
          
                if (_value != formattedValue)
                {
                    _value = formattedValue;
                    ValueChanged?.Invoke(this, _value);
                }

                if (text == formattedValue)
                    return;
            }
      
            isTextValid = false;
            
        }

        private void CustomTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!isTextValid)
            {
                suppressTextChanged = true;
                Text = Value;
                suppressTextChanged = false;
                isTextValid = true;
            }
        }
    }
}