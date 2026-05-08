using MonoRead.App.ViewModels;

namespace MonoRead.App.Views;

public partial class WebSearchPage : ContentPage
{
    public WebSearchPage(WebSearchViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}