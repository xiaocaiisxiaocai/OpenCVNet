using System;
using System.Globalization;
using System.Windows.Data;
using VisionInspection.Core.Models;

namespace VisionInspection.App.Converters
{
    /// <summary>检测方法枚举 → 中文名（界面显示用；选中值仍为枚举）。</summary>
    public sealed class DetectionMethodNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DetectionMethod m)
            {
                switch (m)
                {
                    case DetectionMethod.ForegroundRatio: return "前景占比法";
                    case DetectionMethod.BaselineDiff: return "基准差分法";
                    case DetectionMethod.TemplateMatch: return "模板匹配法";
                }
            }
            return value?.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
