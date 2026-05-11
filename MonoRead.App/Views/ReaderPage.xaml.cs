using MonoRead.App.ViewModels;

namespace MonoRead.App.Views;

public partial class ReaderPage : ContentPage
{
    private bool _isDisposed = false;
    public ReaderPage(ReaderViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();

        if (BindingContext is ReaderViewModel vm)
        {
            // 关键：在页面消失时，立即阻断后续可能改变 Position 的 UI 事件
            // 直接在当前上下文执行保存，不再开新线程以防资源竞争
            try
            {
                await vm.SaveCurrentProgressAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"退出保存失败: {ex.Message}");
            }
        }
    }
}