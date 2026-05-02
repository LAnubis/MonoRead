using MonoRead.App.ViewModels;

namespace MonoRead.App.Views;

public partial class RecentPage : ContentPage
{
    private readonly RecentViewModel _viewModel;

    public RecentPage(RecentViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    // 每次切换到该 Tab 时，自动拉取最新的阅读状态
    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadRecentBooksCommand.Execute(null);
    }
}