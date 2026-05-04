using MonoRead.App.ViewModels;

namespace MonoRead.App.Views;

public partial class TrashPage : ContentPage
{
    private readonly TrashViewModel _viewModel;

    public TrashPage(TrashViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    // 【核心修复】：挂载页面生命周期，每次页面显示时自动触发底层查询
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // 唤醒大脑去数据库捞取软删除的数据
        await _viewModel.LoadTrashDataAsync();
    }
}