using MonoRead.App.ViewModels;

namespace MonoRead.App.Views;

public partial class BookNotesDetailPage : ContentPage
{
    public BookNotesDetailPage(BookNotesDetailViewModel viewModel)
    {
        InitializeComponent();
        // 将注入的 ViewModel 绑定为页面的数据上下文
        BindingContext = viewModel;
    }
}