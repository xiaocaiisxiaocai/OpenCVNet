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

        private const double HandleTolerance = 12;

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

            var p = e.GetPosition(Overlay);
            _start = p;
            var sel = vm.SelectedStation;

            if (sel != null && NearCorner(p, sel))
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
            var p = e.GetPosition(Overlay);

            switch (_mode)
            {
                case EditMode.Moving:
                    _active.X = (int)Math.Max(0, _origX + (p.X - _start.X));
                    _active.Y = (int)Math.Max(0, _origY + (p.Y - _start.Y));
                    break;
                case EditMode.Resizing:
                    _active.Width = (int)Math.Max(5, _origW + (p.X - _start.X));
                    _active.Height = (int)Math.Max(5, _origH + (p.Y - _start.Y));
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
                var p = e.GetPosition(Overlay);
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

        private static bool NearCorner(Point p, StationRowViewModel s)
            => Math.Abs(p.X - (s.X + s.Width)) <= HandleTolerance
               && Math.Abs(p.Y - (s.Y + s.Height)) <= HandleTolerance;

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
    }
}
