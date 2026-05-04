#if ANDROID
using Android.Views;
using Android.Widget;
using Microsoft.Maui.Handlers;
using MonoRead.App.Platforms.Android;
using MonoRead.App.ViewModels;
#endif

using CommunityToolkit.Maui;
using Microsoft.EntityFrameworkCore;
using MonoRead.App.ViewModels;
using MonoRead.App.Views;
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

                        // 3. 【核心修复】：使用 textView.Context 替代 handler.Context，突破 MAUI 接口限制
                        textView.SetOnTouchListener(new ReadingTouchListener(textView.Context));
                    }
                });
#endif
            });

            // ==================== 基础设施与数据库 ====================
            builder.Services.AddDbContext<AppDbContext>(options =>
            {
                // 使用绝对路径获取存储目录，规避跨平台路径解析差异
                string dbPath = Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, "monoread.db3");
                options.UseSqlite($"Data Source={dbPath}");
            });
            builder.Services.AddSingleton<IFileSystemService, FileSystemService>();

            // ==================== 应用层 UseCase 与 仓储 ====================
            builder.Services.AddScoped<IBookRepository, BookRepository>();
            builder.Services.AddScoped<IFolderRepository, FolderRepository>();
            builder.Services.AddScoped<IBookParsingUseCase, BookParsingUseCase>();
            builder.Services.AddScoped<IBookNoteRepository, BookNoteRepository>();

            // ==================== 视图与 ViewModel 注册 ====================
            // 底部 TabBar 一级主模块
            builder.Services.AddTransient<RecentViewModel>();
            builder.Services.AddTransient<RecentPage>();

            builder.Services.AddTransient<LibraryViewModel>();
            builder.Services.AddTransient<LibraryPage>();

            builder.Services.AddTransient<NotesViewModel>();
            builder.Services.AddTransient<NotesPage>();

            builder.Services.AddTransient<SettingsViewModel>();
            builder.Services.AddTransient<SettingsPage>();

            // 深度路由二级业务模块
            builder.Services.AddTransient<ReaderViewModel>();
            builder.Services.AddTransient<ReaderPage>();
            builder.Services.AddTransient<BookNotesDetailPage>();
            builder.Services.AddTransient<BookNotesDetailViewModel>();
 


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