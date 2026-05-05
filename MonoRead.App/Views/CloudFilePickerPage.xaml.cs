using MonoRead.App.ViewModels;

namespace MonoRead.App.Views;

public partial class CloudFilePickerPage : ContentPage
{
    private readonly CloudFilePickerViewModel _viewModel;

    public CloudFilePickerPage(CloudFilePickerViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.InitializeAsync();
    }
}