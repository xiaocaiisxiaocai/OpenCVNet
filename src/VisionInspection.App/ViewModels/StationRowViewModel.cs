using CommunityToolkit.Mvvm.ComponentModel;
using VisionInspection.Core.Models;

namespace VisionInspection.App.ViewModels
{
    /// <summary>
    /// 工位编辑行 VM：把 <see cref="RoiRect"/> 平铺为可绑定属性，便于 DataGrid 逐工位编辑
    /// （板件大小不一 → 每工位 X/Y/Width/Height 各自独立）。
    /// </summary>
    public partial class StationRowViewModel : ObservableObject
    {
        [ObservableProperty] private int _index;
        [ObservableProperty] private int _row;
        [ObservableProperty] private int _column;
        [ObservableProperty] private string _name;
        [ObservableProperty] private int _x;
        [ObservableProperty] private int _y;
        [ObservableProperty] private int _width;
        [ObservableProperty] private int _height;
        [ObservableProperty] private DetectionMethod _method;
        [ObservableProperty] private double _threshold;
        [ObservableProperty] private bool _enabled = true;
        [ObservableProperty] private bool _isSelected;

        public StationRowViewModel()
        {
        }

        public StationRowViewModel(Station s)
        {
            _index = s.Index;
            _row = s.Row;
            _column = s.Column;
            _name = s.Name;
            _x = s.Roi.X;
            _y = s.Roi.Y;
            _width = s.Roi.Width;
            _height = s.Roi.Height;
            _method = s.Method;
            _threshold = s.Threshold;
            _enabled = s.Enabled;
        }

        public Station ToStation() => new Station
        {
            Index = Index,
            Row = Row,
            Column = Column,
            Name = Name,
            Roi = new RoiRect(X, Y, Width, Height),
            Method = Method,
            Threshold = Threshold,
            Enabled = Enabled
        };
    }
}
