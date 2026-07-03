using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VisionInspection.App.ViewModels;

namespace VisionInspection.App.Views
{
    /// <summary>
    /// 图形化 ROI 标定画布：在标定底图上叠加工位框，支持
    /// 空白拖拽画新框、点选、拖拽移动、右下角拖拽缩放。坐标 1:1 对应图像像素（Viewbox 统一缩放显示）。
    /// </summary>
    public partial class RoiCanvas : UserControl
    {
        private enum EditMode { None, Drawing, Moving, Resizing }

        private const double ScreenHandleTolerance = 12;

        private EditMode _mode;
        private Point _start;
        private double _origX, _origY, _origW, _origH;
        private StationRowViewModel _active;

        public RoiCanvas()
        {
            InitializeComponent();
        }

        private RecipeManagementViewModel Vm => DataContext as RecipeManagementViewModel;

        private void OnDown(object sender, MouseButtonEventArgs e)
        {
            var vm = Vm;
            if (vm == null) return;

            var p = ClampPoint(e.GetPosition(Overlay), vm);
            _start = p;
            var sel = vm.SelectedStation;

            if (sel != null && NearCorner(p, sel, ImageTolerance()))
            {
                _mode = EditMode.Resizing;
                _active = sel;
                SaveOrig(sel);
            }
            else
            {
                var hit = TopStationAt(vm, p);
                if (hit != null)
                {
                    vm.SelectedStation = hit;
                    _mode = EditMode.Moving;
                    _active = hit;
                    SaveOrig(hit);
                }
                else
                {
                    _mode = EditMode.Drawing;
                    Canvas.SetLeft(RubberBand, p.X);
                    Canvas.SetTop(RubberBand, p.Y);
                    RubberBand.Width = 0;
                    RubberBand.Height = 0;
                    RubberBand.Visibility = Visibility.Visible;
                }
            }
            Overlay.CaptureMouse();
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            if (_mode == EditMode.None) return;
            var vm = Vm;
            var p = ClampPoint(e.GetPosition(Overlay), vm);

            switch (_mode)
            {
                case EditMode.Moving:
                    _active.X = (int)Clamp(_origX + (p.X - _start.X), 0, Math.Max(0, ImageWidth(vm) - _active.Width));
                    _active.Y = (int)Clamp(_origY + (p.Y - _start.Y), 0, Math.Max(0, ImageHeight(vm) - _active.Height));
                    break;
                case EditMode.Resizing:
                    _active.Width = (int)Clamp(_origW + (p.X - _start.X), 5, Math.Max(5, ImageWidth(vm) - _active.X));
                    _active.Height = (int)Clamp(_origH + (p.Y - _start.Y), 5, Math.Max(5, ImageHeight(vm) - _active.Y));
                    break;
                case EditMode.Drawing:
                    Canvas.SetLeft(RubberBand, Math.Min(p.X, _start.X));
                    Canvas.SetTop(RubberBand, Math.Min(p.Y, _start.Y));
                    RubberBand.Width = Math.Abs(p.X - _start.X);
                    RubberBand.Height = Math.Abs(p.Y - _start.Y);
                    break;
            }
        }

        private void OnUp(object sender, MouseButtonEventArgs e)
        {
            var vm = Vm;
            if (_mode == EditMode.Drawing && vm != null)
            {
                var p = ClampPoint(e.GetPosition(Overlay), vm);
                int x = (int)Math.Min(p.X, _start.X);
                int y = (int)Math.Min(p.Y, _start.Y);
                int w = (int)Math.Abs(p.X - _start.X);
                int h = (int)Math.Abs(p.Y - _start.Y);
                if (w >= 5 && h >= 5) vm.AddStationAt(x, y, w, h);
            }

            RubberBand.Visibility = Visibility.Collapsed;
            _mode = EditMode.None;
            _active = null;
            Overlay.ReleaseMouseCapture();
        }

        private void SaveOrig(StationRowViewModel s)
        {
            _origX = s.X; _origY = s.Y; _origW = s.Width; _origH = s.Height;
        }

        private static bool NearCorner(Point p, StationRowViewModel s, double tolerance)
            => Math.Abs(p.X - (s.X + s.Width)) <= tolerance
               && Math.Abs(p.Y - (s.Y + s.Height)) <= tolerance;

        private static StationRowViewModel TopStationAt(RecipeManagementViewModel vm, Point p)
        {
            for (int i = vm.Stations.Count - 1; i >= 0; i--)
            {
                var s = vm.Stations[i];
                if (p.X >= s.X && p.X <= s.X + s.Width && p.Y >= s.Y && p.Y <= s.Y + s.Height)
                    return s;
            }
            return null;
        }

        private double ImageTolerance()
        {
            double scale = Math.Max(Overlay.ActualWidth / Math.Max(1, ImageWidth(Vm)),
                Overlay.ActualHeight / Math.Max(1, ImageHeight(Vm)));
            if (scale <= 0 || double.IsInfinity(scale) || double.IsNaN(scale)) scale = 1;
            return ScreenHandleTolerance / scale;
        }

        private static Point ClampPoint(Point p, RecipeManagementViewModel vm)
            => new Point(Clamp(p.X, 0, ImageWidth(vm)), Clamp(p.Y, 0, ImageHeight(vm)));

        private static double ImageWidth(RecipeManagementViewModel vm)
            => vm != null && vm.ReferenceImageWidth > 0 ? vm.ReferenceImageWidth : 640;

        private static double ImageHeight(RecipeManagementViewModel vm)
            => vm != null && vm.ReferenceImageHeight > 0 ? vm.ReferenceImageHeight : 480;

        private static double Clamp(double value, double min, double max)
            => Math.Max(min, Math.Min(max, value));
    }
}
