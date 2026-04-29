using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MonoRead.Infrastructure;

namespace MonoRead.App
{
    public partial class App : Application
    {
        private readonly IServiceProvider _serviceProvider;
        public App()
        {
            InitializeComponent();


            // ======= 1.4 核心逻辑：注册全局异常兜底 =======
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
            // 保持你项目默认的 MainPage 赋值（通常是 AppShell 或 MainPage）
            //MainPage = new AppShell();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new AppShell());
        }
        // 处理未观察到的异步 Task 异常
        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            LogExceptionToSandbox(e.Exception, "UnobservedTaskException");
            e.SetObserved(); // 标记为已观察，防止进程直接被系统杀死
        }

        // 处理主线程和其他线程的未处理异常
        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                LogExceptionToSandbox(ex, "UnhandledException");
            }
        }

        // 将崩溃信息写入沙盒的 crash.log 中
        private void LogExceptionToSandbox(Exception ex, string type)
        {
            try
            {
                string logPath = Path.Combine(FileSystem.AppDataDirectory, "crash.log");
                string logContent = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{type}]\n{ex.Message}\n{ex.StackTrace}\n\n";

                File.AppendAllText(logPath, logContent);
                System.Diagnostics.Debug.WriteLine($"[CRASH SAVED]: {logPath}");
            }
            catch
            {
                // 如果写日志本身也报错了（比如磁盘满了），只能放弃，避免陷入死循环
            }
        }
        // 重写 App 启动完成后的生命周期
        protected override void OnStart()
        {
            base.OnStart();

            // 开启后台异步任务，绝对不阻塞 Android 主线程
            Task.Run(async () =>
            {
                // 在后台线程创建独立的作用域
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                try
                {
                    // 使用异步的 MigrateAsync
                    await dbContext.Database.MigrateAsync();

                    // 异步写入测试数据
                    if (!dbContext.Folders.Any())
                    {
                        dbContext.Folders.Add(new MonoRead.Core.Entities.Folder { Name = "默认书架" });
                        await dbContext.SaveChangesAsync();
                    }

                    // 成功的话在控制台输出
                    System.Diagnostics.Debug.WriteLine("====== 数据库沙盒初始化成功 ======");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"====== 数据库初始化失败: {ex.Message} ======");
                }
            });
        }
    }
}