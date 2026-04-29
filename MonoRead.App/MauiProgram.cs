using CommunityToolkit.Maui; // 必须引用
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MonoRead.Core.Interfaces;
using MonoRead.Infrastructure;
using MonoRead.Infrastructure.Services;
using MonoRead.UseCase;
namespace MonoRead.App
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                // 核心修复点：链式调用初始化 Toolkit
                .UseMauiCommunityToolkit()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            // ======= 核心修复点 =======
            // 把 FileSystem 的调用放到 options 的 Lambda 内部！
            // 这样它只会在 AppDbContext 真正被实例化时才会去获取路径
            builder.Services.AddDbContext<AppDbContext>(options =>
            {
                string dbPath = Path.Combine(FileSystem.AppDataDirectory, "monoread.db3");
                options.UseSqlite($"Data Source={dbPath}");
            });
            // 注册基础设施服务 (单例，因为文件系统路径在 App 生命周期内不会变)
            builder.Services.AddSingleton<IFileSystemService, FileSystemService>();
            // 注册应用层 UseCase (作用域生命周期，因为依赖 DbContext)
            // builder.Services.AddScoped<IArchiveBookUseCase, ArchiveBookUseCase>();

            // 注册视图模型和页面 (Transient 表示每次请求都创建新实例)
            builder.Services.AddTransient<ViewModels.LibraryViewModel>();
            builder.Services.AddTransient<Views.LibraryPage>();
            builder.Services.AddTransient<ViewModels.ReaderViewModel>();
            builder.Services.AddTransient<Views.ReaderPage>();

            builder.Services.AddScoped<IBookRepository, BookRepository>();
            builder.Services.AddScoped<IBookParsingUseCase, BookParsingUseCase>();


            // 1. 先把 App 构建出来
            var app = builder.Build();

            // 2. 【核心修复】创建一个服务作用域，获取数据库上下文，并强制执行建表！
            using (var scope = app.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<MonoRead.Infrastructure.AppDbContext>();

                // 这一句是魔法！它会检查底层 SQLite：如果没有 Books 等表，它会瞬间自动全部创建！
                dbContext.Database.EnsureCreated();
            }

            // 3. 返回启动 App
            return app;
        }
    }
}
