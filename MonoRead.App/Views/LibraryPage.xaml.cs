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
}