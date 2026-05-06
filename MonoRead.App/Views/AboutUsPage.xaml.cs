namespace MonoRead.App.Views;

public partial class AboutUsPage : ContentPage
{
    public AboutUsPage()
    {
        InitializeComponent();
    }
    private async void OnBackTapped(object sender, EventArgs e)
    {
        // ".." 是 Shell 路由中代表“返回上一级”的绝对指令
        await Shell.Current.GoToAsync("..");
    }
}