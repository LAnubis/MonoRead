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
    // 【核心新增】当从阅读器返回书架时，强制刷新数据，以更新 UI 上的“读到第几章”
    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);

        if (BindingContext is LibraryViewModel vm)
        {
            vm.LoadBooksCommand.Execute(null);
        }
    }
    // 【核心修复】：弃用容易被导航堆栈吃掉的 OnNavigatedTo。
    // 改用 OnAppearing：这是系统底层的渲染钩子，只要书架界面出现在屏幕上，就绝对会触发。
    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (BindingContext is LibraryViewModel vm)
        {
            // 触发 ViewModel 中的数据重新加载
            vm.LoadBooksCommand.Execute(null);
        }
    }
}