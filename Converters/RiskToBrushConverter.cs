using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using PSuite.Models;

namespace PSuite.Converters
{
    public class RiskToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is not TweakRisk risk)
                return DependencyProperty.UnsetValue;

            string role = parameter as string ?? "Fg";
            string key = $"PSuiteStatus{risk}{role}Brush";

            if (Application.Current.Resources.TryGetValue(key, out var brush) && brush is Brush b)
                return b;

            return DependencyProperty.UnsetValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }
}