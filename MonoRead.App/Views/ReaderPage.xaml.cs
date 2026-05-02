using MonoRead.App.ViewModels;

namespace MonoRead.App.Views;

public partial class ReaderPage : ContentPage
{
    public ReaderPage(ReaderViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;

        // 【架构净化提示】
        // 旧版的 PageContent 监听和 ReaderScrollView 强制滚动逻辑已在此被彻底抹除。
        // 全新的引擎下，一切翻页与定位（包括切片排版后回到第一页），
        // 均由 ViewModel 中的 CurrentPagePosition 属性双向绑定驱动 CarouselView 自动完成。
        // 我们实现了零 UI 耦合的纯正 MVVM。
    }
}