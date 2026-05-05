using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MonoRead.App.Messages;
using MonoRead.Core.Entities;
using MonoRead.Core.Interfaces;
using System.Collections.ObjectModel;

namespace MonoRead.App.ViewModels
{
    public partial class CloudFilePickerViewModel : ObservableObject
    {
        private readonly ICloudStorageService _cloudStorageService;

        [ObservableProperty] private ObservableCollection<WebDavFileNode> _files = new();
        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private string _currentPath = "/"; // 根目录开始

        // 用于控制“返回上一级”按钮的显示
        public bool IsNotRoot => CurrentPath != "/";

        // 缓存账号密码（实际生产中这部分也应该加密传递，为简化这里直接读取）
        private string _url = "";
        private string _user = "";
        private string _pass = "";

        public CloudFilePickerViewModel(ICloudStorageService cloudStorageService)
        {
            _cloudStorageService = cloudStorageService;
        }

        // 页面每次出现时调用
        public async Task InitializeAsync()
        {
            _url = await SecureStorage.Default.GetAsync("WebDav_Url") ?? "";
            _user = await SecureStorage.Default.GetAsync("WebDav_User") ?? "";
            _pass = await SecureStorage.Default.GetAsync("WebDav_Pass") ?? "";

            if (string.IsNullOrEmpty(_url) || string.IsNullOrEmpty(_user) || string.IsNullOrEmpty(_pass))
            {
                await Application.Current.MainPage!.DisplayAlert("提示", "云盘凭据丢失，请重新配置", "确定");
                await Shell.Current.GoToAsync("..");
                return;
            }

            CurrentPath = "/";
            await LoadDirectoryAsync(CurrentPath);
        }

        private async Task LoadDirectoryAsync(string targetPath)
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                var list = await _cloudStorageService.ListFilesAsync(_url, _user, _pass, targetPath);

                // 仅过滤出 文件夹 和 .txt 文件
                var filteredList = list.Where(f => f.IsDirectory || f.DisplayName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                                       .OrderByDescending(f => f.IsDirectory) // 文件夹排前面
                                       .ThenBy(f => f.DisplayName)
                                       .ToList();

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    Files.Clear();
                    foreach (var item in filteredList) Files.Add(item);
                    CurrentPath = targetPath;
                    OnPropertyChanged(nameof(IsNotRoot));
                });
            }
            catch (Exception ex)
            {
                await Application.Current.MainPage!.DisplayAlert("错误", $"读取目录失败: {ex.Message}", "确定");
            }
            finally { IsBusy = false; }
        }

        [RelayCommand]
        private async Task GoBackAsync() => await Shell.Current.GoToAsync("..");

        [RelayCommand]
        private async Task GoUpDirectoryAsync()
        {
            if (CurrentPath == "/") return;
            // 简单处理路径退回：/A/B/ -> /A/
            var parts = CurrentPath.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);
            string parentPath = "/" + string.Join("/", parts) + (parts.Count > 0 ? "/" : "");
            await LoadDirectoryAsync(parentPath);
        }

        [RelayCommand]
        private async Task OpenNodeAsync(WebDavFileNode node)
        {
            if (node.IsDirectory)
            {
                // 【核心修复】：
                // 1. 坚果云 WebDAV 严格要求目录请求必须以 "/" 结尾，否则抛出 409 异常。
                // 2. 直接使用 node.Href，避免 Uri 构造函数解析相对路径时抛出崩溃异常。
                string nextPath = node.Href;
                if (!nextPath.EndsWith("/"))
                {
                    nextPath += "/";
                }
                await LoadDirectoryAsync(nextPath);
            }
            else
            {
                // 选中了 TXT 文件，发送消息给书架，并关闭自己
                bool confirm = await Application.Current.MainPage!.DisplayAlert("确认下载", $"即将从云端下载：\n{node.DisplayName}", "开始导入", "取消");
                if (confirm)
                {
                    WeakReferenceMessenger.Default.Send(new CloudFileSelectedMessage(node.Href, node.DisplayName));
                    await Shell.Current.GoToAsync("..");
                }
            }
        }
    }
}