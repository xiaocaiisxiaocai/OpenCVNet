using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionInspection.Core.Abstractions;

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

        public MainWindowViewModel(IRecipeStore recipeStore, RunViewModel run,
            System.Func<int, int, VisionInspection.Core.Imaging.ImageFrame> captureFunc = null)
        {
            RecipeManagement = new RecipeManagementViewModel(recipeStore, captureFunc);
            Run = run;
            _currentPage = run;
        }

        [RelayCommand]
        private void NavigateToRun() => CurrentPage = Run;

        [RelayCommand]
        private void NavigateToRecipes() => CurrentPage = RecipeManagement;
    }
}
