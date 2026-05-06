#if ANDROID
using Android.Views;
using Android.Widget;
using Microsoft.Maui.Handlers;
#endif

using CommunityToolkit.Maui;
using Microsoft.EntityFrameworkCore;
using MonoRead.App.ViewModels;
using MonoRead.App.Views;
using MonoRead.Core.Interfaces;
using MonoRead.Infrastructure;
using MonoRead.Infrastructure.Services;
using MonoRead.UseCase;
using MonoRead.Infrastructure.services;
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

            // ====================================================================
            // 【硬核架构升级】：接管 Android 原生拖拽选词，并篡改系统弹出菜单
            // ====================================================================
            builder.ConfigureMauiHandlers(handlers =>
            {
#if ANDROID
                LabelHandler.Mapper.AppendToMapping("GranularSelection", (handler, view) =>
                {
                    if (view is Label label && label.StyleClass != null && label.StyleClass.Contains("ReadingText"))
                    {
                        var textView = handler.PlatformView;
                        // 1. 开启原生长按拖拽水滴光标
                        textView.SetTextIsSelectable(true);
                        // 2. 注入我们篡改过的菜单拦截器
                        textView.CustomSelectionActionModeCallback = new MonoReadActionModeCallback(textView);
                    }
                });
#endif
            });

            builder.Services.AddDbContext<AppDbContext>(options =>
            {
                string dbPath = Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, "monoread.db3");
                options.UseSqlite($"Data Source={dbPath}");
            });
            builder.Services.AddSingleton<IFileSystemService, FileSystemService>();

            builder.Services.AddScoped<IBookRepository, BookRepository>();
            builder.Services.AddScoped<IFolderRepository, FolderRepository>();
            builder.Services.AddScoped<IBookParsingUseCase, BookParsingUseCase>();
            builder.Services.AddScoped<IBookNoteRepository, BookNoteRepository>();

            builder.Services.AddTransient<RecentViewModel>();
            builder.Services.AddTransient<RecentPage>();
            builder.Services.AddTransient<LibraryViewModel>();
            builder.Services.AddTransient<LibraryPage>();
            builder.Services.AddTransient<NotesViewModel>();
            builder.Services.AddTransient<NotesPage>();
            builder.Services.AddTransient<SettingsViewModel>();
            builder.Services.AddTransient<SettingsPage>();

            builder.Services.AddTransient<ReaderViewModel>();
            builder.Services.AddTransient<ReaderPage>();
            builder.Services.AddTransient<BookNotesDetailPage>();
            builder.Services.AddTransient<BookNotesDetailViewModel>();
            builder.Services.AddTransient<TrashViewModel>();
            builder.Services.AddTransient<TrashPage>();

            builder.Services.AddSingleton<MonoRead.Core.Interfaces.ICloudStorageService, MonoRead.Infrastructure.Services.WebDavStorageService>();
            builder.Services.AddTransient<MonoRead.App.ViewModels.CloudBackupViewModel>();
            builder.Services.AddTransient<MonoRead.App.Views.CloudBackupPage>();
            builder.Services.AddTransient<MonoRead.App.ViewModels.CloudFilePickerViewModel>();
            builder.Services.AddTransient<MonoRead.App.Views.CloudFilePickerPage>();

            builder.Services.AddSingleton<IZipArchiveService, MonoRead.Infrastructure.Services.ZipArchiveService>();
            builder.Services.AddSingleton<MonoRead.UseCase.ICloudBackupUseCase, MonoRead.UseCase.CloudBackupUseCase>();
            builder.Services.AddScoped<IReadingRecordRepository, ReadingRecordRepository>();
            var app = builder.Build();
            using (var scope = app.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                dbContext.Database.EnsureCreated();
            }

            return app;
        }
    }

#if ANDROID
    // ====================================================================
    // 🔪 Android 系统菜单拦截器 (驻留在底层)
    // ====================================================================
    public class MonoReadActionModeCallback : Java.Lang.Object, ActionMode.ICallback
    {
        private readonly TextView _textView;

        public MonoReadActionModeCallback(TextView textView)
        {
            _textView = textView;
        }

        public bool OnCreateActionMode(ActionMode mode, IMenu menu)
        {
            menu.Clear(); // 杀掉系统自带的复制/全选

            // 植入我们的极简调色盘和操作按钮 (使用 Emoji 呈现极致 UI)
            menu.Add(0, 1, 0, "🟨").SetShowAsAction(ShowAsAction.Always);
            menu.Add(0, 2, 1, "🟩").SetShowAsAction(ShowAsAction.Always);
            menu.Add(0, 3, 2, "🟦").SetShowAsAction(ShowAsAction.Always);
            menu.Add(0, 4, 3, "📝 想法").SetShowAsAction(ShowAsAction.Always);
            menu.Add(0, 5, 4, "复制").SetShowAsAction(ShowAsAction.Always);
            return true;
        }

        public bool OnPrepareActionMode(ActionMode mode, IMenu menu) => false;

        public bool OnActionItemClicked(ActionMode mode, IMenuItem item)
        {
            int start = _textView.SelectionStart;
            int end = _textView.SelectionEnd;
            if (start < 0 || end < 0 || start == end) return false;

            if (start > end) { int temp = start; start = end; end = temp; }
            string selectedText = _textView.Text.Substring(start, end - start);

            string action = item.ItemId switch
            {
                1 => "#FFF9C4", // 黄
                2 => "#E8F5E9", // 绿
                3 => "#E3F2FD", // 蓝
                4 => "NOTE",
                5 => "COPY",
                _ => ""
            };

            // 发射跨端消息，把选中的字和动作传给 MAUI 的 ReaderViewModel
            CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(
                new GranularTextSelectedMessage(selectedText, action));

            mode.Finish(); // 关闭黑底菜单
            _textView.ClearFocus(); // 取消文字的选中状态
            return true;
        }

        public void OnDestroyActionMode(ActionMode mode) { }
    }
#endif
}