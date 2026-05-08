using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonoRead.Core.Entities;
using MonoRead.Core.Interfaces;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace MonoRead.App.ViewModels
{
    public partial class BookSourceManagementViewModel : ObservableObject
    {
        // 【修复】：使用专门的接口
        private readonly IBookSourceRepository _sourceRepository;
        private readonly HttpClient _httpClient;

        [ObservableProperty] private ObservableCollection<BookSource> _sources = new();
        [ObservableProperty] private bool _isBusy;

        public BookSourceManagementViewModel(IBookSourceRepository sourceRepository)
        {
            _sourceRepository = sourceRepository;
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        }

        [RelayCommand]
        public async Task LoadSourcesAsync()
        {
            IsBusy = true;
            try
            {
                // 使用全局数据库锁确保并发安全
                await LibraryViewModel.GlobalDbLock.WaitAsync();
                try
                {
                    var list = await _sourceRepository.GetAllAsync();
                    Sources = new ObservableCollection<BookSource>(list.OrderBy(x => x.Name));
                }
                finally { LibraryViewModel.GlobalDbLock.Release(); }
            }
            finally { IsBusy = false; }
        }

        [RelayCommand]
        private async Task ShowImportOptionsAsync()
        {
            string action = await Shell.Current.DisplayActionSheetAsync("导入书源", "取消", null, "从剪贴板导入", "从网络链接导入");

            if (action == "从剪贴板导入") await ImportFromClipboardAsync();
            else if (action == "从网络链接导入") await ImportFromUrlAsync();
        }

        private async Task ImportFromClipboardAsync()
        {
            string json = await Clipboard.Default.GetTextAsync();
            if (string.IsNullOrWhiteSpace(json))
            {
                await Shell.Current.DisplayAlertAsync("错误", "剪贴板为空", "确定");
                return;
            }
            await ProcessJsonAsync(json);
        }

        private async Task ImportFromUrlAsync()
        {
            string url = await Shell.Current.DisplayPromptAsync("网络导入", "请输入书源 JSON 的直连地址", "导入", "取消", "https://...");
            if (string.IsNullOrWhiteSpace(url)) return;

            IsBusy = true;
            try
            {
                string json = await _httpClient.GetStringAsync(url);
                await ProcessJsonAsync(json);
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlertAsync("下载失败", ex.Message, "确定");
            }
            finally { IsBusy = false; }
        }

        private async Task ProcessJsonAsync(string json)
        {
            try
            {
                // 验证 JSON 格式
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // 【核心修复】：使用 TryGetProperty 增强容错性，并对齐最终定稿的 "baseUrl" 字段
                string sourceName = root.TryGetProperty("sourceName", out var nameProp) ? nameProp.GetString() : "未命名书源";

                // 兼容老版本的 bookSourceUrl 和新版本的 baseUrl
                string baseUrl = "";
                if (root.TryGetProperty("baseUrl", out var baseProp)) baseUrl = baseProp.GetString();
                else if (root.TryGetProperty("bookSourceUrl", out var oldBaseProp)) baseUrl = oldBaseProp.GetString();

                var newSource = new BookSource
                {
                    Id = Guid.NewGuid(),
                    Name = sourceName ?? "未命名书源",
                    BaseUrl = baseUrl ?? "",
                    RulesJson = json,
                    IsEnabled = true, // 确保刚导入的书源默认是开启状态
                    UpdatedAt = DateTime.UtcNow
                };

                await _sourceRepository.AddAsync(newSource);
                await LoadSourcesAsync(); // 刷新列表
                await Shell.Current.DisplayAlertAsync("成功", $"书源【{newSource.Name}】已导入！", "太棒了");
            }
            catch (JsonException)
            {
                await Shell.Current.DisplayAlertAsync("解析失败", "这段文本不是合法的 JSON 格式，请检查是否复制完整。", "确定");
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlertAsync("解析失败", $"未知错误: {ex.Message}", "确定");
            }
        }

        [RelayCommand]
        private async Task DeleteSourceAsync(BookSource source)
        {
            if (source == null) return;
            bool confirm = await Shell.Current.DisplayAlertAsync("删除确认", $"确定要删除书源【{source.Name}】吗？", "删除", "取消");
            if (confirm)
            {
                await _sourceRepository.DeleteAsync(source);
                await LoadSourcesAsync();
            }
        }

        [RelayCommand]
        private async Task GoBackAsync()
        {
            // 调用 MAUI Shell 的自带后退路由
            await Shell.Current.GoToAsync("..");
        }
    }
}