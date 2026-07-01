using VisionInspection.App.ViewModels;
using Wpf.Ui.Controls;

namespace VisionInspection.App.Views
{
    public partial class MainWindow : FluentWindow
    {
        public MainWindow(MainWindowViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
