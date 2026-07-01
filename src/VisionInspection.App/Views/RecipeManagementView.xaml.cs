using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using VisionInspection.App.ViewModels;
using WpfUiMessageBox = Wpf.Ui.Controls.MessageBox;
using WpfUiMessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;

namespace VisionInspection.App.Views
{
    public partial class RecipeManagementView : UserControl
    {
        public RecipeManagementView()
        {
            InitializeComponent();
        }

        private RecipeManagementViewModel Vm => DataContext as RecipeManagementViewModel;

        // 删除前二次确认（WPF-UI 主题化 MessageBox，与整体 Fluent 皮肤统一）。
        private async void OnDelete(object sender, RoutedEventArgs e)
        {
            var vm = Vm;
            if (vm == null || string.IsNullOrEmpty(vm.SelectedModelCode)) return;

            var box = new WpfUiMessageBox
            {
                Title = "删除配方",
                Content = $"确定删除配方 {vm.SelectedModelCode} 吗？此操作不可撤销。",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消"
            };
            if (await box.ShowDialogAsync() == WpfUiMessageBoxResult.Primary)
                vm.DeleteCommand.Execute(null);
        }

        // 文件对话框属于纯 UI 关注点，置于 code-behind；业务逻辑仍在 ViewModel。
        private void OnLoadReferenceImage(object sender, RoutedEventArgs e)
        {
            var vm = Vm;
            if (vm == null) return;
            var dlg = new OpenFileDialog { Filter = "图像|*.png;*.jpg;*.jpeg;*.bmp" };
            if (dlg.ShowDialog() == true) vm.LoadReferenceImage(dlg.FileName);
        }

        private void OnExport(object sender, RoutedEventArgs e)
        {
            var vm = Vm;
            if (vm == null) return;
            var dlg = new SaveFileDialog
            {
                Filter = "配方 JSON|*.json",
                FileName = (string.IsNullOrWhiteSpace(vm.ModelCode) ? "recipe" : vm.ModelCode) + ".json"
            };
            if (dlg.ShowDialog() == true) vm.ExportTo(dlg.FileName);
        }

        private void OnImport(object sender, RoutedEventArgs e)
        {
            var vm = Vm;
            if (vm == null) return;
            var dlg = new OpenFileDialog { Filter = "配方 JSON|*.json" };
            if (dlg.ShowDialog() == true) vm.ImportFrom(dlg.FileName);
        }

        private void OnTeach(object sender, RoutedEventArgs e)
        {
            var vm = Vm;
            if (vm == null) return;

            var present = new OpenFileDialog
            {
                Title = "选择【满件】样张（可多选）",
                Multiselect = true,
                Filter = "图像|*.png;*.jpg;*.jpeg;*.bmp"
            };
            if (present.ShowDialog() != true) return;

            var absent = new OpenFileDialog
            {
                Title = "选择【缺件】样张（可多选）",
                Multiselect = true,
                Filter = "图像|*.png;*.jpg;*.jpeg;*.bmp"
            };
            if (absent.ShowDialog() != true) return;

            vm.TeachFromFiles(present.FileNames, absent.FileNames);
        }
    }
}
