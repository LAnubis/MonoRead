namespace MonoRead.App
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();
            // 注册阅读器页面的路由，这样才能在底层使用 GoToAsync 跳转
            Routing.RegisterRoute(nameof(Views.ReaderPage), typeof(Views.ReaderPage));
            // 【核心修复 3：补齐回收站的全局路由注册】
            Routing.RegisterRoute("TrashPage", typeof(Views.TrashPage));
        }
    }
}
