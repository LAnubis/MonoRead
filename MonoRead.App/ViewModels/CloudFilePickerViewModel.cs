using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MonoRead.App.Messages;
using MonoRead.Core.Entities;
using MonoRead.Core.Interfaces;
using System.Collections.ObjectModel;

namespace MonoRead.App.ViewModels
{
    // =========================================================
    // 【新增】：UI 专用展示模型，用于绑定 CheckBox 的实时的选中状态
    // =========================================================
    public partial class CloudFileUiNode : ObservableObject
    {
        public WebDavFileNode OriginalNode { get; set; }
        public string DisplayName => OriginalNode.DisplayName;
        public bool IsDirectory => OriginalNode.IsDirectory;
        public string ContentLength
        {
            get
            {
                if (OriginalNode.IsDirectory) return ""; // 文件夹不显示大小

                long bytes = OriginalNode.ContentLength;
                if (bytes < 1024) return $"{bytes} B";
                if (bytes < 1048576) return $"{(bytes / 1024.0):F1} KB";
                return $"{(bytes / 1048576.0):F2} MB";
            }
        }
        public string Href => OriginalNode.Href;

        [ObservableProperty]
        private bool _isSelected;
    }

    public partial class CloudFilePickerViewModel : ObservableObject
    {
        private readonly ICloudStorageService _cloudStorageService;

        // 注意这里变成了 CloudFileUiNode
        [ObservableProperty] private ObservableCollection<CloudFileUiNode> _files = new();
        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private string _currentPath = "/";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SelectionStatusText))]
        [NotifyPropertyChangedFor(nameof(HasSelectedFiles))]
        private ObservableCollection<CloudFileUiNode> _selectedFilesList = new();

        public string SelectionStatusText => $"已选 {SelectedFilesList.Count}/10";
        public bool HasSelectedFiles => SelectedFilesList.Count > 0;
        public bool IsNotRoot => CurrentPath != "/";

        private string _url = "";
        private string _user = "";
        private string _pass = "";

        public CloudFilePickerViewModel(ICloudStorageService cloudStorageService)
        {
            _cloudStorageService = cloudStorageService;
        }

        public async Task InitializeAsync()
        {
            _url = await SecureStorage.Default.GetAsync("WebDav_Url") ?? "";
            _user = await SecureStorage.Default.GetAsync("WebDav_User") ?? "";
            _pass = await SecureStorage.Default.GetAsync("WebDav_Pass") ?? "";

            if (string.IsNullOrEmpty(_url) || string.IsNullOrEmpty(_user) || string.IsNullOrEmpty(_pass))
            {
                await Shell.Current.DisplayAlertAsync("提示", "云盘凭据丢失，请重新配置", "确定");
                await Shell.Current.GoToAsync("..");
                return;
            }

            CurrentPath = "/";
            SelectedFilesList.Clear();
            await LoadDirectoryAsync(CurrentPath);
        }

        private async Task LoadDirectoryAsync(string targetPath)
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                var list = await _cloudStorageService.ListFilesAsync(_url, _user, _pass, targetPath);

                var filteredList = list.Where(f => f.IsDirectory || f.DisplayName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                                       .OrderByDescending(f => f.IsDirectory)
                                       .ThenBy(f => f.DisplayName)
                                       .Select(f => new CloudFileUiNode { OriginalNode = f, IsSelected = false }) // 穿上 UI 外衣
                                       .ToList();

                // 【细节体验优化】：如果用户进退文件夹，保持之前打过的勾不消失
                foreach (var item in filteredList)
                {
                    if (SelectedFilesList.Any(s => s.Href == item.Href))
                    {
                        item.IsSelected = true;
                        var oldItem = SelectedFilesList.First(s => s.Href == item.Href);
                        SelectedFilesList.Remove(oldItem);
                        SelectedFilesList.Add(item);
                    }
                }

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
                await Shell.Current.DisplayAlertAsync("错误", $"读取目录失败: {ex.Message}", "确定");
            }
            finally { IsBusy = false; }
        }

        [RelayCommand]
        private async Task GoBackAsync() => await Shell.Current.GoToAsync("..");

        [RelayCommand]
        private async Task GoUpDirectoryAsync()
        {
            if (CurrentPath == "/") return;
            var parts = CurrentPath.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);
            string parentPath = "/" + string.Join("/", parts) + (parts.Count > 0 ? "/" : "");
            await LoadDirectoryAsync(parentPath);
        }

        [RelayCommand]
        private async Task OpenNodeAsync(CloudFileUiNode node)
        {
            if (node.IsDirectory)
            {
                string nextPath = node.Href;
                if (!nextPath.EndsWith("/")) nextPath += "/";
                await LoadDirectoryAsync(nextPath);
            }
            else
            {
                // 控制 CheckBox 的勾选状态
                if (node.IsSelected)
                {
                    node.IsSelected = false;
                    SelectedFilesList.Remove(node);
                }
                else
                {
                    if (SelectedFilesList.Count >= 10)
                    {
                        await Shell.Current.DisplayAlertAsync("超限", "每次最多只能选择 10 本书籍哦。", "知道了");
                        return;
                    }
                    node.IsSelected = true;
                    SelectedFilesList.Add(node);
                }

                OnPropertyChanged(nameof(SelectionStatusText));
                OnPropertyChanged(nameof(HasSelectedFiles));
            }
        }

        [RelayCommand]
        private async Task ConfirmImportAsync()
        {
            if (!SelectedFilesList.Any()) return;

            bool confirm = await Shell.Current.DisplayAlertAsync("确认下载", $"即将从坚果云极速下载 {SelectedFilesList.Count} 本书籍，是否继续？", "开始批量导入", "取消");

            if (confirm)
            {
                var payload = SelectedFilesList.Select(f => (f.Href, f.DisplayName)).ToList();
                WeakReferenceMessenger.Default.Send(new CloudFilesSelectedMessage(payload));
                await Shell.Current.GoToAsync("..");
            }
        }
    }
}