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
                    catch { /* 忽略单个书源的崩溃 */ }
                }

                if (!allResults.Any())
                {
                    await Shell.Current.DisplayAlert("无结果", "各大书源均未检索到相关书籍，请更换关键词或添加新书源。", "确定");
                }
                else
                {
                    foreach (var book in allResults) SearchResults.Add(book);
                }
            }
            finally { IsBusy = false; }
        }

        // 【核心 V2 逻辑】：在线流媒体书籍入库
        [RelayCommand]
        private async Task DownloadBookAsync(Book book)
        {
            if (book == null || IsBusy) return;

            IsBusy = true;
            BusyMessage = "正在秒级获取全本目录...";

            try
            {
                var rule = JsonSerializer.Deserialize<BookSourceRuleModel>(book.ProgressLocator);
                string detailUrl = book.FileHash;

                var chapters = await _searchEngine.GetTocAsync(rule, detailUrl);
                if (chapters == null || !chapters.Any())
                {
                    await Shell.Current.DisplayAlert("添加失败", "无法获取书籍目录，可能是书源规则失效或网站改版。", "知道了");
                    return;
                }

                BusyMessage = "正在将书籍入库...";

                var existingBooks = await _bookRepository.GetAllBooksAsync();
                if (existingBooks.Any(b => b.FileHash == detailUrl))
                {
                    await Shell.Current.DisplayAlert("提示", "书架中已存在相同的小说！", "知道了");
                    return;
                }

                var newBook = new Book
                {
                    Id = Guid.NewGuid(),
                    Title = book.Title,
                    Author = book.Author,
                    Description = book.Description,
                    CoverUrl = book.CoverUrl,
                    FileHash = detailUrl,
                    FilePath = "ONLINE_BOOK", // 【暗号：这是一本云端流媒体书】
                    ProgressLocator = book.ProgressLocator,
                    CreatedAt = DateTime.UtcNow
                };

                var bookChapters = new List<BookChapter>();
                int sort = 0;
                foreach (var c in chapters)
                {
                    bookChapters.Add(new BookChapter
                    {
                        Id = Guid.NewGuid(),
                        BookId = newBook.Id,
                        Title = c.Title,
                        SortOrder = sort++,
                        StartLocator = c.Url // 【暗号：存入真实网页URL】
                    });
                }

                // 调用你的完美方法连书带章节一起插入
                await _bookRepository.SaveBookWithChaptersAsync(newBook, bookChapters);

                await Shell.Current.DisplayAlert("添加成功", $"《{newBook.Title}》已成功加入书架！共拉取 {bookChapters.Count} 章。", "太棒了");
                await Shell.Current.GoToAsync("..");
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlert("添加失败", $"处理过程中发生错误: {ex.Message}", "确定");
            }
            finally { IsBusy = false; }
        }

        [RelayCommand]
        private async Task GoBackAsync() => await Shell.Current.GoToAsync("..");
    }
}