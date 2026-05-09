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
                var sources = await _sourceRepository.GetAllAsync();
                var activeSources = sources.Where(s => s.IsEnabled).ToList();

                if (!activeSources.Any())
                {
                    await Shell.Current.DisplayAlert("提示", "您还没有配置或启用任何网络书源，请先前往设置中添加。", "知道了");
                    return;
                }

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
                            foreach (var b in books) b.ProgressLocator = source.RulesJson;
                            allResults.AddRange(books);
                        }
                    }
                    catch { /* 忽略单个书源故障 */ }
                }

                if (!allResults.Any()) await Shell.Current.DisplayAlert("无结果", "未检索到相关书籍。", "确定");
                else foreach (var book in allResults) SearchResults.Add(book);
            }
            finally { IsBusy = false; }
        }

        // =========================================================
        // 【核心优化】：瞬间加入书架，不抓取章节
        // =========================================================
        [RelayCommand]
        private async Task DownloadBookAsync(Book book)
        {
            if (book == null || IsBusy) return;

            IsBusy = true;
            BusyMessage = "正在加入书架...";

            try
            {
                string detailUrl = book.FileHash; // 搜索时详情页URL临时存放在此

                var existingBooks = await _bookRepository.GetAllBooksAsync();
                if (existingBooks.Any(b => b.FileHash == detailUrl))
                {
                    await Shell.Current.DisplayAlert("提示", "书架中已存在相同的小说！", "知道了");
                    return;
                }

                // 建立云端占位书 (幽灵书)
                var newBook = new Book
                {
                    Id = Guid.NewGuid(),
                    Title = book.Title,
                    Author = book.Author,
                    Description = book.Description,
                    CoverUrl = book.CoverUrl,
                    FileHash = detailUrl,
                    FilePath = "ONLINE_GHOST", // 暗号：表示这是一个待下载的云端占位符
                    ProgressLocator = book.ProgressLocator,
                    CreatedAt = DateTime.UtcNow
                };

                // 【修复 CS7036】：补上第二个参数，传入一个空的章节列表
                await _bookRepository.SaveBookWithChaptersAsync(newBook, new List<BookChapter>());

                await Shell.Current.DisplayAlert("添加成功", $"《{newBook.Title}》已加入书架！\n点击书架上的封面即可开始全本极速下载。", "太棒了");
                await Shell.Current.GoToAsync("..");
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlert("添加失败", ex.Message, "确定");
            }
            finally { IsBusy = false; }
        }

        [RelayCommand]
        private async Task GoBackAsync() => await Shell.Current.GoToAsync("..");
    }
}