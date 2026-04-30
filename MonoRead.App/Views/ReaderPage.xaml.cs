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
                    // 【核心修复：防 Android 闪退锁】
                    // 必须给 Android 原生 UI 引擎 100 毫秒的时间来完成文字高度的测量和渲染
                    // 否则直接调用 ScrollToAsync 会导致底层引擎直接崩溃！
                    await Task.Delay(100);
                    await ReaderScrollView.ScrollToAsync(0, 0, false);
                });
            }
        };
    }
}