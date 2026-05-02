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
        }
    }
}
