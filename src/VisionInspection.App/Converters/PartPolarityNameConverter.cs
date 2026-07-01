using System;
using System.Globalization;
using System.Windows.Data;
using VisionInspection.Vision.Teaching;

namespace VisionInspection.App.Converters
{
    /// <summary>件极性枚举 → 中文名（界面显示用；选中值仍为枚举）。</summary>
    public sealed class PartPolarityNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is PartPolarity p)
            {
                switch (p)
                {
                    case PartPolarity.Auto: return "自动";
                    case PartPolarity.BrightParts: return "亮件";
                    case PartPolarity.DarkParts: return "暗件";
                }
            }
            return value?.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
