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

            builder.Services.AddScoped<IBookRepository, BookRepository>();
            builder.Services.AddScoped<IBookParsingUseCase, BookParsingUseCase>();
            return builder.Build();
        }
    }
}
