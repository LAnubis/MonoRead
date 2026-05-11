using MonoRead.App.ViewModels;

namespace MonoRead.App.Views;

public partial class LibraryPage : ContentPage
{
    // 通过构造函数注入 ViewModel
    public LibraryPage(LibraryViewModel viewModel)
    {
        InitializeComponent();
        // 将注入的 ViewModel 设为当前页面的数据上下文
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (BindingContext is LibraryViewModel vm)
        {
            // 【核心修复 2】：改用静默刷新！
            // 以后从阅读器退回书架，或者从设置切回书架，再也不会有烦人的黑框弹出来了！
            vm.SilentRefreshCommand.Execute(null);

            // 检查外部文件导入保持不变
            vm.CheckPendingImportCommand.Execute(null);
        }
    }
}