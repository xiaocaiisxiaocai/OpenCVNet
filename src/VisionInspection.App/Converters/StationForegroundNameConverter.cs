using System;
using System.Globalization;
using System.Windows.Data;
using VisionInspection.App.ViewModels;

namespace VisionInspection.App.Converters
{
    public sealed class StationForegroundNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is StationForegroundMode mode)
            {
                switch (mode)
                {
                    case StationForegroundMode.Inherit: return "继承";
                    case StationForegroundMode.Bright: return "亮前景";
                    case StationForegroundMode.Dark: return "暗前景";
                }
            }
            return value?.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
