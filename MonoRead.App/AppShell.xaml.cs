using MonoRead.App.Views;

namespace MonoRead.App
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();
            // 注册独立压栈的非 Tab 页路由
            Routing.RegisterRoute(nameof(ReaderPage), typeof(ReaderPage));
            Routing.RegisterRoute("TrashPage", typeof(TrashPage));
            Routing.RegisterRoute(nameof(BookNotesDetailPage), typeof(BookNotesDetailPage));
            // 确保深层页面被注册在路由字典中
            Routing.RegisterRoute(nameof(Views.ReaderPage), typeof(Views.ReaderPage));
            Routing.RegisterRoute(nameof(Views.BookNotesDetailPage), typeof(Views.BookNotesDetailPage));
            Routing.RegisterRoute("AboutUsPage", typeof(Views.AboutUsPage));
            Routing.RegisterRoute("CloudBackupPage", typeof(Views.CloudBackupPage));
            Routing.RegisterRoute("CloudFilePickerPage", typeof(Views.CloudFilePickerPage));
            Routing.RegisterRoute(nameof(ReadingStatsPage), typeof(ReadingStatsPage));
        }
    }
}
