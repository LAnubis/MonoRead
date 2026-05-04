using MonoRead.App.ViewModels;

namespace MonoRead.App.Views;

public partial class NotesPage : ContentPage
{
    private readonly NotesViewModel _viewModel;

    public NotesPage(NotesViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    // 页面呈现钩子：强制触发生命周期内的数据最新拉取
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadNotedBooksAsync();
    }
}