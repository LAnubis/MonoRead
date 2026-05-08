using MonoRead.App.ViewModels;

namespace MonoRead.App.Views;

public partial class BookSourceManagementPage : ContentPage
{
    public BookSourceManagementPage(BookSourceManagementViewModel viewModel)
    {
        InitializeComponent();

        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // 每次进入这个页面，自动触发加载列表的操作
        if (BindingContext is BookSourceManagementViewModel vm)
        {
            vm.LoadSourcesCommand.Execute(null);
        }
    }
}