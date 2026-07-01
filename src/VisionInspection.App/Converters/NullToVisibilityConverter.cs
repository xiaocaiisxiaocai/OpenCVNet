using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VisionInspection.App.Converters
{
    /// <summary>null → Visible（显示占位提示），非 null → Collapsed。</summary>
    public sealed class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value == null ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
