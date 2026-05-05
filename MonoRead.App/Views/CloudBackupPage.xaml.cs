using MonoRead.App.ViewModels;

namespace MonoRead.App.Views;

public partial class CloudBackupPage : ContentPage
{
    public CloudBackupPage(CloudBackupViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}