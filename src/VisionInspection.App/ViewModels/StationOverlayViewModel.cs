using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VisionInspection.App.ViewModels
{
    /// <summary>实时图像上叠加的工位框（图像像素坐标，颜色随判定：绿=有件/红=缺件）。</summary>
    public partial class StationOverlayViewModel : ObservableObject
    {
        [ObservableProperty] private double _x;
        [ObservableProperty] private double _y;
        [ObservableProperty] private double _width;
        [ObservableProperty] private double _height;
        [ObservableProperty] private Brush _stroke;
        [ObservableProperty] private string _label;
    }
}
