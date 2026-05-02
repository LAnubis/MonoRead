using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonoRead.Core.Entities;
using MonoRead.Core.Interfaces;
using MonoRead.Infrastructure.Logging;
using System.Collections.ObjectModel;

namespace MonoRead.App.ViewModels
{
    [QueryProperty(nameof(BookIdString), "BookId")]
    public partial class ReaderViewModel : ObservableObject
    {
        private readonly IBookRepository _bookRepository;

        // ==================== 状态机 ====================
        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private string _bookIdString = string.Empty;

        [ObservableProperty]
        private Book? _currentBook;

        [ObservableProperty]
        private BookChapter? _currentChapter;

        [ObservableProperty]
        private string _chapterTitle = "加载中...";

        // ==================== 阅读器翻页引擎核心 ====================

        // 渲染缓冲池：承载切好片的一页页内容
        [ObservableProperty]
        private ObservableCollection<ReaderPageNode> _bookPages = new();

        // 翻页指针：由 CarouselView 驱动
        [ObservableProperty]
        private int _currentPagePosition = 0;

        // ==================== 导航与控件状态 ====================
        [ObservableProperty]
        private bool _canGoPrevious;

        [ObservableProperty]
        private bool _canGoNext;

        [ObservableProperty]
        private bool _isMenuVisible = false;

        [ObservableProperty]
        private bool _isTocVisible = false;

        [ObservableProperty]
        private ObservableCollection<BookChapter> _chaptersList = new();

        // ==================== UI 偏好设置 (持久化) ====================
        [ObservableProperty]
        private int _readerFontSize = Preferences.Default.Get("ReaderFontSize", 18);

        [ObservableProperty]
        private Color _pageBackgroundColor = Color.FromArgb(Preferences.Default.Get("ThemeBg", "#FFFFFF"));

        [ObservableProperty]
        private Color _textPrimaryColor = Color.FromArgb(Preferences.Default.Get("ThemeText", "#333333"));

        [ObservableProperty]
        private Color _statusTextColor = Color.FromArgb(Preferences.Default.Get("ThemeStatus", "#888888"));

        public ReaderViewModel(IBookRepository bookRepository)
        {
            _bookRepository = bookRepository;
        }

        partial void OnBookIdStringChanged(string value)
        {
            if (Guid.TryParse(value, out Guid parsedId))
            {
                LoadBookDataAsync(parsedId);
            }
        }

        private async void LoadBookDataAsync(Guid bookId)
        {
            try
            {
                CurrentBook = await _bookRepository.GetBookWithChaptersAsync(bookId);

                if (CurrentBook != null && CurrentBook.Chapters.Any())
                {
                    var sortedChapters = CurrentBook.Chapters.OrderBy(c => c.SortOrder).ToList();
                    ChaptersList = new ObservableCollection<BookChapter>(sortedChapters);

                    var targetChapter = sortedChapters.First();

                    if (!string.IsNullOrWhiteSpace(CurrentBook.ProgressLocator) && CurrentBook.ProgressLocator != "{}")
                    {
                        try
                        {
                            using var doc = System.Text.Json.JsonDocument.Parse(CurrentBook.ProgressLocator);
                            if (doc.RootElement.TryGetProperty("chapterId", out var chapterIdElement))
                            {
                                var savedChapterId = chapterIdElement.GetGuid();
                                var savedChapter = sortedChapters.FirstOrDefault(c => c.Id == savedChapterId);
                                if (savedChapter != null) targetChapter = savedChapter;
                            }
                        }
                        catch (Exception ex)
                        {
                            LocalLogger.LogError($"进度解析异常: {ex.Message}");
                        }
                    }
                    await LoadChapterContentAsync(targetChapter, clearBuffer: true);
                }
            }
            catch (Exception ex)
            {
                LocalLogger.LogError($"书籍加载失败: {ex.Message}");
            }
        }

        // ==================== 核心引擎：加载与切片 ====================
        private async Task LoadChapterContentAsync(BookChapter targetChapter, bool clearBuffer)
        {
            if (IsLoading) return;
            IsLoading = true;

            if (CurrentBook == null)
            {
                IsLoading = false;
                return;
            }

            try
            {
                CurrentChapter = targetChapter;
                ChapterTitle = targetChapter.Title;

                CanGoPrevious = CurrentBook.Chapters.Any(c => c.SortOrder < targetChapter.SortOrder);
                CanGoNext = CurrentBook.Chapters.Any(c => c.SortOrder > targetChapter.SortOrder);

                // 提取全量文本
                string extractedContent = await Task.Run(async () =>
                {
                    long startCharIndex = 0;
                    if (!string.IsNullOrWhiteSpace(targetChapter.StartLocator) && targetChapter.StartLocator != "{}")
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(targetChapter.StartLocator);
                        startCharIndex = doc.RootElement.GetProperty("position").GetInt64();
                    }

                    var nextChapter = CurrentBook.Chapters.FirstOrDefault(c => c.SortOrder == targetChapter.SortOrder + 1);
                    long endCharIndex = long.MaxValue;
                    if (nextChapter != null && !string.IsNullOrWhiteSpace(nextChapter.StartLocator) && nextChapter.StartLocator != "{}")
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(nextChapter.StartLocator);
                        endCharIndex = doc.RootElement.GetProperty("position").GetInt64();
                    }

                    int lengthToRead = endCharIndex == long.MaxValue ? 20000 : (int)(endCharIndex - startCharIndex);
                    if (lengthToRead <= 0) return "（本章暂无正文内容）";
                    if (lengthToRead > 20000) lengthToRead = 20000;

                    using var reader = new StreamReader(CurrentBook.FilePath);
                    if (startCharIndex > 0)
                    {
                        char[] throwawayBuffer = new char[8192];
                        long remainingToSkip = startCharIndex;
                        while (remainingToSkip > 0)
                        {
                            int toRead = (int)Math.Min(throwawayBuffer.Length, remainingToSkip);
                            int readCount = await reader.ReadAsync(throwawayBuffer, 0, toRead);
                            if (readCount == 0) break;
                            remainingToSkip -= readCount;
                        }
                    }

                    char[] contentBuffer = new char[lengthToRead];
                    int charsRead = await reader.ReadAsync(contentBuffer, 0, lengthToRead);
                    return new string(contentBuffer, 0, charsRead);
                });

                // 【算法切入】：调用分页器将长文本切为单页
                var newPages = Utils.TextPaginator.Paginate(extractedContent, targetChapter.Id, targetChapter.Title, ReaderFontSize);

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (clearBuffer)
                    {
                        BookPages.Clear();
                    }

                    foreach (var p in newPages) BookPages.Add(p);

                    if (clearBuffer)
                    {
                        CurrentPagePosition = 0;
                    }
                });

                CurrentBook.ProgressLocator = $"{{\"chapterId\": \"{targetChapter.Id}\"}}";
                await _bookRepository.UpdateBookProgressAsync(CurrentBook.Id, CurrentBook.ProgressLocator);
            }
            catch (Exception ex)
            {
                LocalLogger.LogError($"加载章节失败: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        // ==================== 无缝连读监控机制 ====================
        partial void OnCurrentPagePositionChanged(int value)
        {
            // 越界保护
            if (BookPages == null || BookPages.Count == 0 || value < 0) return;

            // 1. 同步顶部/底部显示状态
            if (value < BookPages.Count)
            {
                ChapterTitle = BookPages[value].ChapterTitle;
            }

            // 2. 如果滑到了本章末尾，静默追加下一章
            if (value == BookPages.Count - 1 && !IsLoading && CanGoNext)
            {
                var nextChapter = CurrentBook?.Chapters.Where(c => c.SortOrder > CurrentChapter?.SortOrder).OrderBy(c => c.SortOrder).FirstOrDefault();
                if (nextChapter != null)
                {
                    // 注意：clearBuffer 为 false，实现无缝追加
                    _ = LoadChapterContentAsync(nextChapter, clearBuffer: false);
                }
            }
        }

        // ==================== 手动切换章节 ====================
        [RelayCommand]
        private async Task PreviousChapterAsync()
        {
            if (CurrentBook == null || CurrentChapter == null || IsLoading) return;
            var prevChapter = CurrentBook.Chapters.Where(c => c.SortOrder < CurrentChapter.SortOrder).OrderByDescending(c => c.SortOrder).FirstOrDefault();
            if (prevChapter != null)
            {
                await LoadChapterContentAsync(prevChapter, clearBuffer: true);
            }
        }

        [RelayCommand]
        private async Task NextChapterAsync()
        {
            if (CurrentBook == null || CurrentChapter == null || IsLoading) return;
            var nextChapter = CurrentBook.Chapters.Where(c => c.SortOrder > CurrentChapter.SortOrder).OrderBy(c => c.SortOrder).FirstOrDefault();
            if (nextChapter != null)
            {
                await LoadChapterContentAsync(nextChapter, clearBuffer: true);
            }
        }

        // ==================== 界面与菜单控制 ====================
        [RelayCommand]
        private async Task GoBackAsync() => await Shell.Current.GoToAsync("..");

        [RelayCommand]
        private void ToggleMenu() => IsMenuVisible = !IsMenuVisible;

        [RelayCommand]
        private void ToggleToc()
        {
            IsTocVisible = !IsTocVisible;
            if (IsTocVisible) IsMenuVisible = false;
        }

        [RelayCommand]
        private async Task SelectChapterAsync(BookChapter chapter)
        {
            if (chapter == null) return;
            IsTocVisible = false;
            IsMenuVisible = false;
            await LoadChapterContentAsync(chapter, clearBuffer: true);
        }

        // ==================== 排版与主题设置 ====================
        [RelayCommand]
        private void IncreaseFont()
        {
            if (ReaderFontSize < 36)
            {
                ReaderFontSize += 2;
                Preferences.Default.Set("ReaderFontSize", ReaderFontSize);
                // 重新排版当前章
                if (CurrentChapter != null) _ = LoadChapterContentAsync(CurrentChapter, clearBuffer: true);
            }
        }

        [RelayCommand]
        private void DecreaseFont()
        {
            if (ReaderFontSize > 12)
            {
                ReaderFontSize -= 2;
                Preferences.Default.Set("ReaderFontSize", ReaderFontSize);
                // 重新排版当前章
                if (CurrentChapter != null) _ = LoadChapterContentAsync(CurrentChapter, clearBuffer: true);
            }
        }

        [RelayCommand]
        private void ChangeTheme(string themeType)
        {
            switch (themeType)
            {
                case "Day": UpdateTheme("#FFFFFF", "#333333", "#888888"); break;
                case "Night": UpdateTheme("#1A1A1C", "#999999", "#555555"); break;
                case "EyeCare": UpdateTheme("#F4ECD8", "#3E3222", "#8D7E68"); break;
            }
        }

        private void UpdateTheme(string bgHex, string textHex, string statusHex)
        {
            PageBackgroundColor = Color.FromArgb(bgHex);
            TextPrimaryColor = Color.FromArgb(textHex);
            StatusTextColor = Color.FromArgb(statusHex);

            Preferences.Default.Set("ThemeBg", bgHex);
            Preferences.Default.Set("ThemeText", textHex);
            Preferences.Default.Set("ThemeStatus", statusHex);
        }

        [RelayCommand]
        private void ChangeTextColor(string hexColor)
        {
            TextPrimaryColor = Color.FromArgb(hexColor);
            Preferences.Default.Set("ThemeText", hexColor);
        }
    }
}