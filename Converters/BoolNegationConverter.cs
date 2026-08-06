using System;
using Microsoft.UI.Xaml.Data;

namespace PSuite.Converters
{
    public class BoolNegationConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => value is bool b ? !b : true;

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => value is bool b ? !b : true;
    }
}