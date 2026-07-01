using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionInspection.App.Hosting;

namespace VisionInspection.App.ViewModels
{
    /// <summary>
    /// 主窗口 VM：通过 <see cref="CurrentPage"/> + DataTemplate 实现 MVVM 导航。
    /// </summary>
    public partial class MainWindowViewModel : ObservableObject
    {
        [ObservableProperty]
        private object _currentPage;

        public RecipeManagementViewModel RecipeManagement { get; }
        public RunViewModel Run { get; }
        public SettingsViewModel Settings { get; }

        public MainWindowViewModel(ApplicationHost host, RunViewModel run)
        {
            RecipeManagement = new RecipeManagementViewModel(host.RecipeStore, host.CaptureFrame);
            Run = run;
            Settings = new SettingsViewModel(host);
            _currentPage = run;
        }

        [RelayCommand]
        private void NavigateToRun() => CurrentPage = Run;

        [RelayCommand]
        private void NavigateToRecipes() => CurrentPage = RecipeManagement;

        [RelayCommand]
        private void NavigateToSettings() => CurrentPage = Settings;
    }
}
