using MonoRead.App.ViewModels;

namespace MonoRead.App.Views;

public partial class ReadingStatsPage : ContentPage
{
    private readonly ReadingStatsViewModel _viewModel;

    public ReadingStatsPage(ReadingStatsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }
    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadDataAsync();
    }
}