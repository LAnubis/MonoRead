using MonoRead.App.ViewModels;

namespace MonoRead.App.Views;

public partial class ReaderPage : ContentPage
{
    public ReaderPage(ReaderViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        // 【核心修复】：监听 ViewModel 的属性变化。一旦正文被重新排版，强行将滚动条拉回 (0,0) 顶部！
        viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ReaderViewModel.PageContent))
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await ReaderScrollView.ScrollToAsync(0, 0, false);
                });
            }
        };
    }
}