using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonoRead.Core.Entities;
using MonoRead.Core.Interfaces;
using MonoRead.Infrastructure.Logging;
using System.Collections.ObjectModel;

namespace MonoRead.App.ViewModels
{
    // 专为瀑布流阅读准备的章节节点
    public class ReaderChapterNode
    {
        public Guid ChapterId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }

    [QueryProperty(nameof(BookIdString), "BookId")]
    public partial class ReaderViewModel : ObservableObject
    {
        private readonly IBookRepository _bookRepository;

        // ==================== 状态机 ====================
        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private bool _isLoadingNext; // 底部触底加载下一章的菊花图状态

        [ObservableProperty]
        private string _bookIdString = string.Empty;

        [ObservableProperty]
        private Book? _currentBook;

        // 当前正在阅读（或最后加载）的章节实体，用于判断上一章/下一章
        [ObservableProperty]
        private BookChapter? _currentChapter;

        // 绑定给底部信息栏显示的当前章节名称
        [ObservableProperty]
        private string _chapterTitle = "加载中...";

        // ==================== 瀑布流阅读引擎核心 ====================
        [ObservableProperty]
        private ObservableCollection<ReaderChapterNode> _readingStream = new();

        private int _lastLoadedSortOrder = -1;

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

                    await LoadChapterIntoStreamAsync(targetChapter, clearStream: true);
                }
            }
            catch (Exception ex)
            {
                LocalLogger.LogError($"书籍加载失败: {ex.Message}");
            }
        }

        // ==================== 核心辅助：文本流提取引擎 ====================
        private async Task<string> ExtractChapterContentAsync(BookChapter chapter)
        {
            if (CurrentBook == null) return string.Empty;

            return await Task.Run(async () =>
            {
                long startCharIndex = 0;
                if (!string.IsNullOrWhiteSpace(chapter.StartLocator) && chapter.StartLocator != "{}")
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(chapter.StartLocator);
                    startCharIndex = doc.RootElement.GetProperty("position").GetInt64();
                }

                var nextChapter = CurrentBook.Chapters.FirstOrDefault(c => c.SortOrder == chapter.SortOrder + 1);
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
        }

        // ==================== 瀑布流装载引擎 ====================
        private async Task LoadChapterIntoStreamAsync(BookChapter targetChapter, bool clearStream)
        {
            if (CurrentBook == null) return;

            if (clearStream) IsLoading = true;
            else IsLoadingNext = true;

            try
            {
                string extractedContent = await ExtractChapterContentAsync(targetChapter);

                var newNode = new ReaderChapterNode
                {
                    ChapterId = targetChapter.Id,
                    Title = targetChapter.Title,
                    Content = extractedContent
                };

                // 更新全局状态：记录底部的章节，控制导航按钮显隐
                CurrentChapter = targetChapter;
                ChapterTitle = targetChapter.Title;
                CanGoPrevious = CurrentBook.Chapters.Any(c => c.SortOrder < targetChapter.SortOrder);
                CanGoNext = CurrentBook.Chapters.Any(c => c.SortOrder > targetChapter.SortOrder);

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (clearStream) ReadingStream.Clear();
                    ReadingStream.Add(newNode);
                    _lastLoadedSortOrder = targetChapter.SortOrder;
                });

                // 进度落盘
                CurrentBook.ProgressLocator = $"{{\"chapterId\": \"{targetChapter.Id}\"}}";
                await _bookRepository.UpdateBookProgressAsync(CurrentBook.Id, CurrentBook.ProgressLocator);

                // 【防抖缓冲】：如果在追加下一章，短暂挂起线程防止 CollectionView 的 Threshold 被瞬间连发触发
                if (!clearStream) await Task.Delay(300);
            }
            catch (Exception ex)
            {
                LocalLogger.LogError($"加载章节失败: {ex.Message}");
            }
            finally
            {
                if (clearStream) IsLoading = false;
                else IsLoadingNext = false;
            }
        }

        // ==================== 无缝连读：双重触底预加载 ====================
        [RelayCommand]
        private async Task LoadNextChapterSeamlesslyAsync()
        {
            // 防重入锁
            if (IsLoadingNext || IsLoading || CurrentBook == null) return;

            var nextChapter = CurrentBook.Chapters
                .Where(c => c.SortOrder > _lastLoadedSortOrder)
                .OrderBy(c => c.SortOrder)
                .FirstOrDefault();

            if (nextChapter != null)
            {
                await LoadChapterIntoStreamAsync(nextChapter, clearStream: false);
            }
        }

        // ==================== 恢复：手动导航 ====================
        [RelayCommand]
        private async Task PreviousChapterAsync()
        {
            if (CurrentBook == null || IsLoading || !ReadingStream.Any()) return;

            // 手动上一章：以当前流的【最顶部】那章为基准往回找
            var firstNodeId = ReadingStream.First().ChapterId;
            var firstChapter = CurrentBook.Chapters.FirstOrDefault(c => c.Id == firstNodeId);
            if (firstChapter == null) return;

            var prevChapter = CurrentBook.Chapters.Where(c => c.SortOrder < firstChapter.SortOrder).OrderByDescending(c => c.SortOrder).FirstOrDefault();
            if (prevChapter != null)
            {
                await LoadChapterIntoStreamAsync(prevChapter, clearStream: true);
            }
        }

        [RelayCommand]
        private async Task NextChapterAsync()
        {
            if (CurrentBook == null || IsLoading || !ReadingStream.Any()) return;

            // 手动下一章：以当前流的【最底部】那章为基准往下找
            var lastNodeId = ReadingStream.Last().ChapterId;
            var lastChapter = CurrentBook.Chapters.FirstOrDefault(c => c.Id == lastNodeId);
            if (lastChapter == null) return;

            var nextChapter = CurrentBook.Chapters.Where(c => c.SortOrder > lastChapter.SortOrder).OrderBy(c => c.SortOrder).FirstOrDefault();
            if (nextChapter != null)
            {
                await LoadChapterIntoStreamAsync(nextChapter, clearStream: true);
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
            await LoadChapterIntoStreamAsync(chapter, clearStream: true);
        }

        // ==================== 排版与主题设置 ====================
        [RelayCommand]
        private void IncreaseFont()
        {
            if (ReaderFontSize < 36)
            {
                ReaderFontSize += 2;
                Preferences.Default.Set("ReaderFontSize", ReaderFontSize);
            }
        }

        [RelayCommand]
        private void DecreaseFont()
        {
            if (ReaderFontSize > 12)
            {
                ReaderFontSize -= 2;
                Preferences.Default.Set("ReaderFontSize", ReaderFontSize);
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