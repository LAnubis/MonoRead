using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using CommunityToolkit.Mvvm.Messaging;
using MonoRead.App.Messages;
using MonoRead.Infrastructure.Logging;
using System.Diagnostics;

namespace MonoRead.App
{
    // 【核心新增：IntentFilter】告诉安卓系统，MonoRead 可以打开 txt 文件
    [IntentFilter(new[] { Intent.ActionView },
        Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
        DataMimeType = "text/plain")]
    //[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    [Activity(Theme = "@style/Maui.SplashTheme",
          MainLauncher = true,
          // 【核心修复：修改为 SingleTask】强制 Android 把它拉回 MonoRead 自己的独立进程栈，绝不寄生在文件管理器下！
          LaunchMode = LaunchMode.SingleTask,
          ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // App 冷启动时，检查是否是被外部文件唤起的
            ProcessIntent(Intent);
        }

        // App 已经在后台时，被外部文件唤起
        protected override void OnNewIntent(Intent? intent)
        {
            base.OnNewIntent(intent);
            ProcessIntent(intent);
        }

        private void ProcessIntent(Intent? intent)
        {
            if (intent?.Action != Intent.ActionView || intent.Data == null) return;

            Android.Net.Uri uri = intent.Data;

            // 立刻在后台线程接管数据，防止阻塞 UI
            Task.Run(async () =>
            {
                try
                {
                    var contentResolver = Android.App.Application.Context.ContentResolver;
                    if (contentResolver == null) return;

                    string fileName = "外部导入小说.txt";
                    using (var cursor = contentResolver.Query(uri, null, null, null, null))
                    {
                        if (cursor != null && cursor.MoveToFirst())
                        {
                            int nameIndex = cursor.GetColumnIndex(Android.Provider.OpenableColumns.DisplayName);
                            if (nameIndex != -1) fileName = cursor.GetString(nameIndex) ?? fileName;
                        }
                    }

                    // 【核心修复：落地为安】不再跨端传递脆弱的 Stream，而是立刻拷贝到本地沙盒
                    var stream = contentResolver.OpenInputStream(uri);
                    if (stream != null)
                    {
                        // 1. 生成物理路径
                        string newFileName = $"{Guid.NewGuid()}.dat";
                        string sandboxPath = Path.Combine(FileSystem.AppDataDirectory, newFileName);

                        // 2. 瞬间落盘
                        using (var fs = new FileStream(sandboxPath, FileMode.Create, FileAccess.Write))
                        {
                            await stream.CopyToAsync(fs);
                        }
                        stream.Dispose();

                        // 【核心修改：双重保险投递】
                        // 1. 存入静态变量（针对冷启动，ViewModel 自己会来取）
                        App.PendingImportFilePath = sandboxPath;
                        App.PendingImportFileName = fileName;

                        // 2. 发送消息（针对热启动，App 在后台存活时）
                        WeakReferenceMessenger.Default.Send(new Messages.FileImportMessage(sandboxPath, fileName));

                        // 3. 等待 MAUI 界面准备就绪
                        while (Microsoft.Maui.Controls.Application.Current?.MainPage == null)
                        {
                            await Task.Delay(200);
                        }
                        await Task.Delay(500);

                        // 4. 发送安全的【物理路径】给 ViewModel
                        // 注意：这里需要你修改一下之前写的 FileImportMessage 类，把 Stream 换成 string SandboxPath
                        WeakReferenceMessenger.Default.Send(new Messages.FileImportMessage(sandboxPath, fileName));
                    }
                }
                catch (Exception ex)
                {
                    LocalLogger.LogError($"导入异常: {ex.Message}");
                }
            });
        }
    }
}
