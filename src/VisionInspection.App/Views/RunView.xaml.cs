using System;
using System.Windows.Controls;
using VisionInspection.App.ViewModels;
using VisionInspection.Runtime;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace VisionInspection.App.Views
{
    public partial class RunView : UserControl
    {
        private readonly SnackbarService _snackbar = new SnackbarService();
        private RunViewModel _subscribed;

        public RunView()
        {
            InitializeComponent();
            _snackbar.SetSnackbarPresenter(RootSnackbar);
            // View 随导航反复重建，用 Loaded/Unloaded 精确挂接与解绑，避免重复订阅。
            Loaded += (s, e) => Attach(DataContext as RunViewModel);
            Unloaded += (s, e) => Attach(null);
            DataContextChanged += (s, e) => Attach(e.NewValue as RunViewModel);
        }

        private void Attach(RunViewModel vm)
        {
            if (ReferenceEquals(_subscribed, vm)) return;
            if (_subscribed != null) _subscribed.AlarmRaised -= OnAlarmRaised;
            _subscribed = vm;
            if (_subscribed != null) _subscribed.AlarmRaised += OnAlarmRaised;
        }

        // 报警瞬时弹窗（已在 UI 线程）：NG / 异常醒目提示，操作工不易漏看。
        private void OnAlarmRaised(RuntimeAlarm alarm)
        {
            _snackbar.Show(
                alarm.Level,
                alarm.Message,
                ControlAppearance.Danger,
                new SymbolIcon(SymbolRegular.ErrorCircle24),
                TimeSpan.FromSeconds(4));
        }
    }
}
