using MonoRead.App.ViewModels;
using MonoRead.Infrastructure.Logging;

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
                    try
                    {
                        // 稍微加长一点等待时间，给低端真机喘息的机会
                        await Task.Delay(150);
                        await ReaderScrollView.ScrollToAsync(0, 0, false);
                    }
                    catch (Exception ex)
                    {
                        // 【核心防御：生吞原生异常】
                        // 如果 Android 原生引擎还没准备好导致滚动报错，直接生吞，只打日志。
                        // 宁可用户手动滑一下回到顶部，也绝对不能让 App 闪退！
                        LocalLogger.LogError($"[严重警告] ScrollToAsync 被系统拒绝: {ex.Message}");
                    }
                });
            }
        };
    }
}