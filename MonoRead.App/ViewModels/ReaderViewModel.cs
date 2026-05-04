using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MonoRead.Core.Entities;
using MonoRead.Core.Interfaces;
using MonoRead.Infrastructure.Logging;
using System.Collections.ObjectModel;

namespace MonoRead.App.ViewModels
{
    // ==================== 跨域消息实体定义 ====================
    public record SingleTapMessage();                  // 收到原生极短单击
    public record TextSelectionStartedMessage();       // 原生划线选词启动了（长按/双击被触发）
    public record TextSelectedMessage(string SelectedText); // 点击了“写笔记”

    public class ReaderChapterNode
    {
        public Guid ChapterId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;

        public List<string> Paragraphs => Content
            .Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => "　　" + p.Trim())
            .ToList();
    }

    [QueryProperty(nameof(BookIdString), "BookId")]
    public partial class ReaderViewModel : ObservableObject
    {
        private readonly IBookRepository _bookRepository;

        // ==================== 状态机 ====================
        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private bool _isLoadingNext;
        [ObservableProperty] private string _bookIdString = string.Empty;
        [ObservableProperty] private Book? _currentBook;
        [ObservableProperty] private BookChapter? _currentChapter;
        [ObservableProperty] private string _chapterTitle = "加载中...";

        // ==================== 瀑布流阅读引擎核心 ====================
        [ObservableProperty] private ObservableCollection<ReaderChapterNode> _readingStream = new();
        private int _lastLoadedSortOrder = -1;

        // ==================== 导航与控件状态 ====================
        [ObservableProperty] private bool _canGoPrevious;
        [ObservableProperty] private bool _canGoNext;
        [ObservableProperty] private bool _isMenuVisible = false;
        [ObservableProperty] private bool _isTocVisible = false;
        [ObservableProperty] private ObservableCollection<BookChapter> _chaptersList = new();

        // ==================== UI 偏好设置 ====================
        [ObservableProperty] private int _readerFontSize = Preferences.Default.Get("ReaderFontSize", 18);
        [ObservableProperty] private Color _pageBackgroundColor = Color.FromArgb(Preferences.Default.Get("ThemeBg", "#F4ECD8"));
        [ObservableProperty] private Color _textPrimaryColor = Color.FromArgb(Preferences.Default.Get("ThemeText", "#3E3222"));
        [ObservableProperty] private Color _statusTextColor = Color.FromArgb(Preferences.Default.Get("ThemeStatus", "#888888"));

        // ==================== 笔记工作台状态 ====================
        [ObservableProperty] private bool _isNoteOverlayVisible = false;
        [ObservableProperty] private string _extractTargetParagraph = string.Empty;
        [ObservableProperty] private string _userNoteInput = string.Empty;

        // ==================== 防抖锁 ====================
        private CancellationTokenSource? _tapCancelToken;
        private bool _isWaitingForTap = false;

        public ReaderViewModel(IBookRepository bookRepository)
        {
            _bookRepository = bookRepository;

            // 1. 监听 Android 原生底层发来的单击事件
            WeakReferenceMessenger.Default.Register<SingleTapMessage>(this, async (r, m) =>
            {
                await HandleSingleTapWithDelayAsync();
            });

            // 2. 【核心拦截】：一旦原生系统唤起了划线菜单，说明用户在双击/长按，立即取消呼出工具栏
            WeakReferenceMessenger.Default.Register<TextSelectionStartedMessage>(this, (r, m) =>
            {
                CancelPendingTap();
            });

            // 3. 监听选中文字，弹出笔记工作台
            WeakReferenceMessenger.Default.Register<TextSelectedMessage>(this, (r, m) =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    CancelPendingTap(); // 确保工具栏绝对不闪现
                    if (!string.IsNullOrWhiteSpace(m.SelectedText))
                    {
                        ExtractTargetParagraph = m.SelectedText;
                        UserNoteInput = string.Empty;
                        IsMenuVisible = false;
                        IsNoteOverlayVisible = true;
                    }
                });
            });
        }

        partial void OnBookIdStringChanged(string value)
        {
            if (Guid.TryParse(value, out Guid parsedId)) LoadBookDataAsync(parsedId);
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
                        catch (Exception ex) { LocalLogger.LogError($"进度解析异常: {ex.Message}"); }
                    }

                    await LoadChapterIntoStreamAsync(targetChapter, clearStream: true);
                }
            }
            catch (Exception ex) { LocalLogger.LogError($"书籍加载失败: {ex.Message}"); }
        }

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

        private async Task LoadChapterIntoStreamAsync(BookChapter targetChapter, bool clearStream)
        {
            if (CurrentBook == null) return;
            if (clearStream) IsLoading = true; else IsLoadingNext = true;

            try
            {
                string extractedContent = await ExtractChapterContentAsync(targetChapter);

                var newNode = new ReaderChapterNode
                {
                    ChapterId = targetChapter.Id,
                    Title = targetChapter.Title,
                    Content = extractedContent
                };

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

                CurrentBook.ProgressLocator = $"{{\"chapterId\": \"{targetChapter.Id}\"}}";
                await _bookRepository.UpdateBookProgressAsync(CurrentBook.Id, CurrentBook.ProgressLocator);
                if (!clearStream) await Task.Delay(300);
            }
            catch (Exception ex) { LocalLogger.LogError($"加载章节失败: {ex.Message}"); }
            finally { if (clearStream) IsLoading = false; else IsLoadingNext = false; }
        }

        [RelayCommand]
        private async Task LoadNextChapterSeamlesslyAsync()
        {
            if (IsLoadingNext || IsLoading || CurrentBook == null) return;
            var nextChapter = CurrentBook.Chapters.Where(c => c.SortOrder > _lastLoadedSortOrder).OrderBy(c => c.SortOrder).FirstOrDefault();
            if (nextChapter != null) await LoadChapterIntoStreamAsync(nextChapter, clearStream: false);
        }

        [RelayCommand]
        private async Task PreviousChapterAsync()
        {
            if (CurrentBook == null || IsLoading || !ReadingStream.Any()) return;
            var firstNodeId = ReadingStream.First().ChapterId;
            var firstChapter = CurrentBook.Chapters.FirstOrDefault(c => c.Id == firstNodeId);
            if (firstChapter == null) return;
            var prevChapter = CurrentBook.Chapters.Where(c => c.SortOrder < firstChapter.SortOrder).OrderByDescending(c => c.SortOrder).FirstOrDefault();
            if (prevChapter != null) await LoadChapterIntoStreamAsync(prevChapter, clearStream: true);
        }

        [RelayCommand]
        private async Task NextChapterAsync()
        {
            if (CurrentBook == null || IsLoading || !ReadingStream.Any()) return;
            var lastNodeId = ReadingStream.Last().ChapterId;
            var lastChapter = CurrentBook.Chapters.FirstOrDefault(c => c.Id == lastNodeId);
            if (lastChapter == null) return;
            var nextChapter = CurrentBook.Chapters.Where(c => c.SortOrder > lastChapter.SortOrder).OrderBy(c => c.SortOrder).FirstOrDefault();
            if (nextChapter != null) await LoadChapterIntoStreamAsync(nextChapter, clearStream: true);
        }

        // ==================== 极简触控与调度 ====================

        private void CancelPendingTap()
        {
            _tapCancelToken?.Cancel();
            _isWaitingForTap = false;
        }

        // 绑定给 XAML 里外层空白区域以及关闭按钮的通用点击处理
        [RelayCommand]
        private async Task ProcessTapAsync()
        {
            await HandleSingleTapWithDelayAsync();
        }

        // 核心防抖判定逻辑
        private async Task HandleSingleTapWithDelayAsync()
        {
            // 如果笔记面板开着，优先关掉
            if (IsNoteOverlayVisible)
            {
                MainThread.BeginInvokeOnMainThread(() => IsNoteOverlayVisible = false);
                return;
            }

            // 如果正在等，说明点第二下了（双击），直接取消呼出工具栏
            if (_isWaitingForTap)
            {
                CancelPendingTap();
                return;
            }

            _isWaitingForTap = true;
            _tapCancelToken = new CancellationTokenSource();

            try
            {
                // 等待 250ms
                await Task.Delay(250, _tapCancelToken.Token);

                if (_isWaitingForTap)
                {
                    _isWaitingForTap = false;
                    MainThread.BeginInvokeOnMainThread(() => ToggleMenu());
                }
            }
            catch (TaskCanceledException)
            {
                // 被双击或原生选词打断，静默不呼出菜单
            }
        }

        [RelayCommand]
        private void CloseNoteWorkbench() => IsNoteOverlayVisible = false;

        [RelayCommand]
        private async Task SaveNoteAsync()
        {
            if (CurrentBook == null || CurrentChapter == null) return;

            if (string.IsNullOrWhiteSpace(ExtractTargetParagraph)) return;

            if (string.IsNullOrWhiteSpace(UserNoteInput))
            {
                await Application.Current.MainPage!.DisplayAlert("提示", "笔记内容不能为空", "好的");
                return;
            }

            var note = new BookNote
            {
                Id = Guid.NewGuid(),
                BookId = CurrentBook.Id,
                ChapterId = CurrentChapter.Id,
                BookTitle = CurrentBook.Title,
                SelectedText = ExtractTargetParagraph,
                UserComment = UserNoteInput,
                CreatedAt = DateTime.UtcNow,
                IsDeleted = false
            };

            // 【待解耦】：如需真实落盘，取消此行注释并在上方注入 IBookNoteRepository
            // await _noteRepository.AddAsync(note); 

            await Application.Current.MainPage!.DisplayAlert("保存成功", "笔记已记录", "确定");
            CloseNoteWorkbench();
        }

        // ==================== 界面与菜单控制 ====================
        private void ToggleMenu() => IsMenuVisible = !IsMenuVisible;
        [RelayCommand] private async Task GoBackAsync() => await Shell.Current.GoToAsync("..");
        [RelayCommand] private void ToggleToc() { IsTocVisible = !IsTocVisible; if (IsTocVisible) IsMenuVisible = false; }
        [RelayCommand] private async Task SelectChapterAsync(BookChapter chapter) { if (chapter == null) return; IsTocVisible = false; IsMenuVisible = false; await LoadChapterIntoStreamAsync(chapter, clearStream: true); }
        [RelayCommand] private void IncreaseFont() { if (ReaderFontSize < 36) { ReaderFontSize += 2; Preferences.Default.Set("ReaderFontSize", ReaderFontSize); } }
        [RelayCommand] private void DecreaseFont() { if (ReaderFontSize > 12) { ReaderFontSize -= 2; Preferences.Default.Set("ReaderFontSize", ReaderFontSize); } }
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
            PageBackgroundColor = Color.FromArgb(bgHex); TextPrimaryColor = Color.FromArgb(textHex); StatusTextColor = Color.FromArgb(statusHex);
            Preferences.Default.Set("ThemeBg", bgHex); Preferences.Default.Set("ThemeText", textHex); Preferences.Default.Set("ThemeStatus", statusHex);
        }
        [RelayCommand] private void ChangeTextColor(string hexColor) { TextPrimaryColor = Color.FromArgb(hexColor); Preferences.Default.Set("ThemeText", hexColor); }
    }
}