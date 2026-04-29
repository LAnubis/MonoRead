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
                // 1. 唤起系统的文件选择器（限制只能选 TXT）
                var customFileType = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.Android, new[] { "text/plain" } },
                { DevicePlatform.WinUI, new[] { ".txt" } },
                { DevicePlatform.MacCatalyst, new[] { "public.plain-text" } }
            });

                var result = await FilePicker.Default.PickAsync(new PickOptions
                {
                    PickerTitle = "请选择要导入的小说 (TXT)",
                    FileTypes = customFileType
                });

                if (result != null)
                {
                    // 2. 模拟分配一个 BookId
                    var newBookId = Guid.NewGuid();

                    // 3. 将文件拷贝到沙盒
                    string savedPath = await _fileSystemService.CopyFileToSandboxAsync(result.FullPath, newBookId);

                    // 4. 计算哈希值防重
                    string fileHash = await _fileSystemService.CalculateFileHashAsync(savedPath);

                    await Shell.Current.DisplayAlert("导入成功", $"文件已存入沙盒！\n路径: {savedPath}\n哈希: {fileHash}", "太棒了");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"导入异常: {ex.Message}");
                await Shell.Current.DisplayAlert("错误", "读取文件失败，请重试。", "确定");
            }
        }
    }
}
