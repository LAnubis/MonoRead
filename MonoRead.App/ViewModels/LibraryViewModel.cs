using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonoRead.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace MonoRead.App.ViewModels
{
    // 必须是 partial class，因为 Toolkit 会自动帮我们生成底层代码
    public partial class LibraryViewModel : ObservableObject
    {
        private readonly IFileSystemService _fileSystemService;
        // 构造函数注入我们刚刚写的服务
        public LibraryViewModel(IFileSystemService fileSystemService)
        {
            _fileSystemService = fileSystemService;
        }



        [RelayCommand]
        private async Task ImportBookAsync()
        {
            try
            {
                var customFileType = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.Android, new[] { "text/plain" } },
                { DevicePlatform.WinUI, new[] { ".txt" } },
                { DevicePlatform.MacCatalyst, new[] { "public.plain-text" } }
            });

                // 1. 唤起选择器 (此时 App 安全挂起)
                var result = await FilePicker.Default.PickAsync(new PickOptions
                {
                    PickerTitle = "请选择要导入的小说 (TXT)",
                    FileTypes = customFileType
                });

                // 如果用户取消了选择，直接返回
                if (result == null) return;

                var newBookId = Guid.NewGuid();
                string newFileName = $"{newBookId}{Path.GetExtension(result.FileName)}";

                // 2. 索要安全流并写入沙盒
                using var stream = await result.OpenReadAsync();
                string savedPath = await _fileSystemService.CopyFileToSandboxAsync(stream, newFileName);
                string fileHash = await _fileSystemService.CalculateFileHashAsync(savedPath);

                // 3. 【绝杀修复】摒弃 BeginInvoke，使用当前窗口的安全调度器 (Dispatcher)
                // 确保 UI 渲染资源已经彻底恢复后再弹窗，消灭 provider null 报错！
                if (Application.Current?.MainPage != null)
                {
                    await Application.Current.MainPage.Dispatcher.DispatchAsync(async () =>
                    {
                        await Application.Current.MainPage.DisplayAlert(
                            "导入成功",
                            $"文件已存入沙盒！\n哈希: {fileHash}",
                            "太棒了");
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"导入异常: {ex.Message}");
                if (Application.Current?.MainPage != null)
                {
                    await Application.Current.MainPage.Dispatcher.DispatchAsync(async () =>
                    {
                        await Application.Current.MainPage.DisplayAlert("错误", $"读取文件失败：{ex.Message}", "确定");
                    });
                }
            }
        }
    }
}
