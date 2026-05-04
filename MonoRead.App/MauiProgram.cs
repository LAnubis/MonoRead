#if ANDROID
using Android.Views;
using Android.Widget;
using Microsoft.Maui.Handlers;
using MonoRead.App.Platforms.Android;
using MonoRead.App.ViewModels; // 用于发送底层消息
#endif

using CommunityToolkit.Maui;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MonoRead.App.ViewModels;
using MonoRead.App.Views;
using MonoRead.Core.Interfaces;
using MonoRead.Infrastructure;
using MonoRead.Infrastructure.Services;
using MonoRead.UseCase;
using CommunityToolkit.Mvvm.Messaging;

namespace MonoRead.App
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkit()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            // ==================== 核心体验升级：原生 Handler 劫持 ====================
            builder.ConfigureMauiHandlers(handlers =>
            {
#if ANDROID
                LabelHandler.Mapper.AppendToMapping("SelectableText", (handler, view) =>
                {
                    // 仅对标记了 "ReadingText" 样式的 Label 开启原生能力
                    if (view is Label label && label.StyleClass != null && label.StyleClass.Contains("ReadingText"))
                    {
                        var textView = handler.PlatformView;

                        // 1. 开启原生水滴光标长按选择
                        textView.SetTextIsSelectable(true);

                        // 2. 劫持系统默认的复制悬浮菜单
                        textView.CustomSelectionActionModeCallback = new MonoReadActionModeCallback(textView);

                        // 3. 【核心补足】：拦截被底层 Android 吸收的短按 Click，跨域发送给 MAUI 触发 250ms 防抖！
                        textView.Click += (sender, e) => {
                            CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(new SingleTapMessage());
                        };
                    }
                });
#endif
            });

            // ==================== 基础设施与数据库 ====================
            builder.Services.AddDbContext<AppDbContext>(options =>
            {
                string dbPath = Path.Combine(FileSystem.AppDataDirectory, "monoread.db3");
                options.UseSqlite($"Data Source={dbPath}");
            });
            builder.Services.AddSingleton<IFileSystemService, FileSystemService>();

            // ==================== 应用层 UseCase 与 仓储 ====================
            builder.Services.AddScoped<IBookRepository, BookRepository>();
            builder.Services.AddScoped<IFolderRepository, FolderRepository>();
            builder.Services.AddScoped<IBookParsingUseCase, BookParsingUseCase>();

            // ==================== 视图与 ViewModel 注册 ====================
            // 底部 TabBar 一级主模块
            builder.Services.AddTransient<RecentViewModel>();
            builder.Services.AddTransient<RecentPage>();

            builder.Services.AddTransient<LibraryViewModel>();
            builder.Services.AddTransient<LibraryPage>();

            builder.Services.AddTransient<NotesPage>(); // 占位

            builder.Services.AddTransient<SettingsViewModel>();
            builder.Services.AddTransient<SettingsPage>();

            // 深度路由二级业务模块
            builder.Services.AddTransient<ReaderViewModel>();
            builder.Services.AddTransient<ReaderPage>();

            builder.Services.AddTransient<BookNotesDetailPage>();

            // ==================== 数据库自动迁移建表 ====================
            var app = builder.Build();
            using (var scope = app.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                dbContext.Database.EnsureCreated();
            }

            return app;
        }
    }
}