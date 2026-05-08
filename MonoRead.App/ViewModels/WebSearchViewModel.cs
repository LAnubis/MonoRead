using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonoRead.Core.Entities;
using MonoRead.Core.Interfaces;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace MonoRead.App.ViewModels
{
    public partial class WebSearchViewModel : ObservableObject
    {
        private readonly IBookSearchEngine _searchEngine;
        private readonly IBookSourceRepository _sourceRepository;
        private readonly IBookRepository _bookRepository;
        private readonly IFileSystemService _fileSystemService;
        private readonly IBookParsingUseCase _bookParsingUseCase;

        [ObservableProperty] private string _searchKeyword = string.Empty;
        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private string _busyMessage = "正在全网检索...";
        [ObservableProperty] private ObservableCollection<Book> _searchResults = new();

        public WebSearchViewModel(
            IBookSearchEngine searchEngine,
            IBookSourceRepository sourceRepository,
            IBookRepository bookRepository,
            IFileSystemService fileSystemService,
            IBookParsingUseCase bookParsingUseCase)
        {
            _searchEngine = searchEngine;
            _sourceRepository = sourceRepository;
            _bookRepository = bookRepository;
            _fileSystemService = fileSystemService;
            _bookParsingUseCase = bookParsingUseCase;
        }

        [RelayCommand]
        private async Task PerformSearchAsync()
        {
            if (string.IsNullOrWhiteSpace(SearchKeyword) || IsBusy) return;

            IsBusy = true;
            BusyMessage = "正在驱动解析引擎...";
            SearchResults.Clear();

            try
            {
                // 1. 获取所有已启用的书源
                var sources = await _sourceRepository.GetAllAsync();
                var activeSources = sources.Where(s => s.IsEnabled).ToList();

                if (!activeSources.Any())
                {
                    await Shell.Current.DisplayAlert("提示", "您还没有配置或启用任何网络书源，请先前往设置中添加。", "知道了");
                    return;
                }

                // 2. 遍历书源进行搜索 (这里为了稳妥采用顺序检索，未来可以升级为 Task.WhenAll 并发检索)
                var allResults = new List<Book>();
                foreach (var source in activeSources)
                {
                    BusyMessage = $"正在检索: {source.Name}...";
                    try
                    {
                        var rule = JsonSerializer.Deserialize<BookSourceRuleModel>(source.RulesJson);
                        if (rule != null)
                        {
                            var books = await _searchEngine.SearchBooksAsync(rule, SearchKeyword);
                            // 把书源的规则偷偷塞在 ProgressLocator 里，方便下载时用
                            foreach (var b in books) b.ProgressLocator = source.RulesJson;
                            allResults.AddRange(books);
                        }
                    }
                    catch { /* 忽略单个书源的崩溃 */ }
                }

                if (!allResults.Any())
                {
                    await Shell.Current.DisplayAlert("无结果", "各大书源均未检索到相关书籍，请更换关键词或添加新书源。", "确定");
                }
                else
                {
                    // 将结果推送到 UI
                    foreach (var book in allResults) SearchResults.Add(book);
                }
            }
            finally { IsBusy = false; }
        }

        [RelayCommand]
        private async Task DownloadBookAsync(Book book)
        {
            if (book == null || IsBusy) return;

            IsBusy = true;
            BusyMessage = "正在解析真实下载地址...";

            try
            {
                var rule = JsonSerializer.Deserialize<BookSourceRuleModel>(book.ProgressLocator);
                // 注意：我们在搜索时，把详情页的链接临时存在了 FileHash 字段里
                string detailUrl = book.FileHash;

                // 1. 获取 TXT 真实下载链接
                string txtUrl = await _searchEngine.GetDownloadUrlAsync(rule.RuleDetail, detailUrl);
                if (string.IsNullOrWhiteSpace(txtUrl))
                {
                    await Shell.Current.DisplayAlert("下载失败", "无法从网页中提取到有效的 TXT 下载链接，可能是规则失效或网站改版。", "知道了");
                    return;
                }

                BusyMessage = "正在极速下载 TXT 文件...";

                // 2. 下载文件流
                using var httpClient = new HttpClient();
                var fileBytes = await httpClient.GetByteArrayAsync(txtUrl);
                using var stream = new MemoryStream(fileBytes);

                BusyMessage = "正在拆解章节并入库...";

                // 3. 接入 MonoRead 的核心拆解管线！
                string newFileName = $"{Guid.NewGuid()}.dat";
                string sandboxPath = await _fileSystemService.CopyFileToSandboxAsync(stream, newFileName);
                string trueHash = await _fileSystemService.CalculateFileHashAsync(sandboxPath);

                // 查重保护
                var existingBooks = await _bookRepository.GetAllBooksAsync();
                if (existingBooks.Any(b => b.FileHash == trueHash))
                {
                    if (File.Exists(sandboxPath)) File.Delete(sandboxPath);
                    await Shell.Current.DisplayAlert("提示", "书架中已存在相同内容的小说！", "知道了");
                    return;
                }

                // 执行极速拆解
                var parsedBook = await Task.Run(async () =>
                    await _bookParsingUseCase.ParseAndSplitBookAsync(sandboxPath, book.Title + ".txt", trueHash));

                // 把抓取到的网络数据补全给拆解后的实体
                parsedBook.Author = book.Author;
                parsedBook.Description = book.Description;
                parsedBook.CoverUrl = book.CoverUrl; // 注入灵魂封面！

                await _bookRepository.UpdateAsync(parsedBook);

                await Shell.Current.DisplayAlert("下载成功", $"《{parsedBook.Title}》已成功加入书架！", "太棒了");

                // 下载成功后自动返回书架
                await Shell.Current.GoToAsync("..");
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlert("处理失败", $"下载或拆解过程中发生错误: {ex.Message}", "确定");
            }
            finally { IsBusy = false; }
        }

        [RelayCommand]
        private async Task GoBackAsync() => await Shell.Current.GoToAsync("..");
    }
}