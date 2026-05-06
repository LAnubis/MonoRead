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
    public record MenuToggleRequestedMessage();
    public record TextSelectionStartedMessage();
    public record GranularTextSelectedMessage(string SelectedText, string ActionCommand);

    public partial class ParagraphUiModel : ObservableObject
    {
        public string RawText { get; set; } = string.Empty;
        [ObservableProperty] private FormattedString _formattedText = new();
    }

    public class ReaderChapterNode
    {
        public Guid ChapterId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public ObservableCollection<ParagraphUiModel> Paragraphs { get; set; } = new();
    }

    // =========================================================
    // 【架构升级】：专供 UI 绑定的前端视图模型 (View Model)
    //  与你底层的 ReaderPageNode (Domain Model) 完美解耦！
    // =========================================================
    public class ReaderPageUiModel
    {
        public Guid ChapterId { get; set; }
        public string Title { get; set; } = string.Empty;
        public ObservableCollection<ParagraphUiModel> Paragraphs { get; set; } = new();
        public string PageIndicator { get; set; } = string.Empty; // 底部显示的 1/5 进度
    }

    [QueryProperty(nameof(BookIdString), "BookId")]
    public partial class ReaderViewModel : ObservableObject
    {
        private readonly IBookRepository _bookRepository;
        private readonly IBookNoteRepository _noteRepository;
        private readonly IReadingRecordRepository _recordRepository;
        private DateTime _sessionStartTime;

        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private bool _isLoadingNext;
        [ObservableProperty] private string _bookIdString = string.Empty;
        [ObservableProperty] private Book? _currentBook;
        [ObservableProperty] private BookChapter? _currentChapter;
        [ObservableProperty] private string _chapterTitle = "加载中...";

        // =========================================================
        // 双数据流引擎：瀑布流与翻页流
        // =========================================================
        [ObservableProperty] private ObservableCollection<ReaderChapterNode> _readingStream = new();

        // 绑定给前端的是我们新增的 UiModel
        [ObservableProperty] private ObservableCollection<ReaderPageUiModel> _pagedStream = new();

        [ObservableProperty] private bool _isScrollMode;

        private int _lastLoadedSortOrder = -1;

        [ObservableProperty] private bool _canGoPrevious;
        [ObservableProperty] private bool _canGoNext;
        [ObservableProperty] private bool _isMenuVisible = false;
        [ObservableProperty] private bool _isTocVisible = false;
        [ObservableProperty] private ObservableCollection<BookChapter> _chaptersList = new();

        [ObservableProperty] private int _readerFontSize = Preferences.Default.Get("ReaderFontSize", 18);
        [ObservableProperty] private Color _pageBackgroundColor = Color.FromArgb(Preferences.Default.Get("ThemeBg", "#F4ECD8"));
        [ObservableProperty] private Color _textPrimaryColor = Color.FromArgb(Preferences.Default.Get("ThemeText", "#3E3222"));
        [ObservableProperty] private Color _statusTextColor = Color.FromArgb(Preferences.Default.Get("ThemeStatus", "#888888"));

        [ObservableProperty] private bool _isNoteOverlayVisible = false;
        [ObservableProperty] private string _extractTargetParagraph = string.Empty;
        [ObservableProperty] private string _userNoteInput = string.Empty;

        private DateTime _lastToggleTime = DateTime.MinValue;

        public ReaderViewModel(IBookRepository bookRepository, IBookNoteRepository noteRepository, IReadingRecordRepository recordRepository)
        {
            _bookRepository = bookRepository;
            _noteRepository = noteRepository;
            _recordRepository = recordRepository;

            _sessionStartTime = DateTime.UtcNow;

            IsScrollMode = Preferences.Default.Get("IsScrollMode", false);

            WeakReferenceMessenger.Default.Register<MenuToggleRequestedMessage>(this, (r, m) => HandleMenuToggleRequest());
            WeakReferenceMessenger.Default.Register<TextSelectionStartedMessage>(this, (r, m) => MainThread.BeginInvokeOnMainThread(() => IsMenuVisible = false));

            WeakReferenceMessenger.Default.Register<GranularTextSelectedMessage>(this, async (r, m) =>
            {
                ExtractTargetParagraph = m.SelectedText;

                if (m.ActionCommand == "COPY") { await Clipboard.Default.SetTextAsync(m.SelectedText); }
                else if (m.ActionCommand == "NOTE")
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        UserNoteInput = string.Empty;
                        IsNoteOverlayVisible = true;
                        IsMenuVisible = false;
                    });
                }
                else { await SaveHighlightSilentlyAsync(m.ActionCommand, m.SelectedText); }
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

                string fileName = Path.GetFileName(CurrentBook.FilePath);
                string actualDevicePath = Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, fileName);

                if (!File.Exists(actualDevicePath)) return "【文件缺失】未能找到该书籍的本地源文件。请确认云端备份是否完整包含了小说文件。";

                int lengthToRead = endCharIndex == long.MaxValue ? 20000 : (int)(endCharIndex - startCharIndex);
                if (lengthToRead <= 0) return "（本章暂无正文内容）";
                if (lengthToRead > 20000) lengthToRead = 20000;

                using var reader = new StreamReader(actualDevicePath);
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

        private FormattedString BuildFormattedParagraph(string paragraphText, List<BookNote> chapterNotes)
        {
            var formattedString = new FormattedString();
            var matchedNotes = chapterNotes.Where(n => !string.IsNullOrWhiteSpace(n.SelectedText) && paragraphText.Contains(n.SelectedText)).ToList();

            if (!matchedNotes.Any())
            {
                formattedString.Spans.Add(new Span { Text = paragraphText });
                return formattedString;
            }

            var noteOccurrences = new List<(int Index, BookNote Note)>();
            foreach (var note in matchedNotes)
            {
                int idx = paragraphText.IndexOf(note.SelectedText);
                if (idx != -1) noteOccurrences.Add((idx, note));
            }
            noteOccurrences = noteOccurrences.OrderBy(n => n.Index).ToList();

            int currentIndex = 0;
            foreach (var occurrence in noteOccurrences)
            {
                if (occurrence.Index < currentIndex) continue;
                if (occurrence.Index > currentIndex)
                {
                    formattedString.Spans.Add(new Span { Text = paragraphText.Substring(currentIndex, occurrence.Index - currentIndex) });
                }
                string colorHex = string.IsNullOrEmpty(occurrence.Note.Color) ? "#FFF9C4" : occurrence.Note.Color;
                formattedString.Spans.Add(new Span { Text = occurrence.Note.SelectedText, BackgroundColor = Color.FromArgb(colorHex) });
                currentIndex = occurrence.Index + occurrence.Note.SelectedText.Length;
            }

            if (currentIndex < paragraphText.Length)
            {
                formattedString.Spans.Add(new Span { Text = paragraphText.Substring(currentIndex) });
            }
            return formattedString;
        }

        private async Task LoadChapterIntoStreamAsync(BookChapter targetChapter, bool clearStream)
        {
            if (CurrentBook == null) return;
            if (clearStream) IsLoading = true; else IsLoadingNext = true;

            try
            {
                string extractedContent = await ExtractChapterContentAsync(targetChapter);

                var allBookNotes = await _noteRepository.GetNotesByBookIdAsync(CurrentBook.Id);
                var chapterNotes = allBookNotes.Where(n => n.ChapterId == targetChapter.Id && !n.IsDeleted).ToList();

                var rawParagraphs = extractedContent
                    .Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => "　　" + p.Trim())
                    .ToList();

                CurrentChapter = targetChapter;
                ChapterTitle = targetChapter.Title;
                CanGoPrevious = CurrentBook.Chapters.Any(c => c.SortOrder < targetChapter.SortOrder);
                CanGoNext = CurrentBook.Chapters.Any(c => c.SortOrder > targetChapter.SortOrder);

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (IsScrollMode)
                    {
                        // ==================== 瀑布流模式 ====================
                        if (clearStream) ReadingStream.Clear();

                        var paragraphsUi = new List<ParagraphUiModel>();
                        foreach (var text in rawParagraphs)
                        {
                            paragraphsUi.Add(new ParagraphUiModel { RawText = text, FormattedText = BuildFormattedParagraph(text, chapterNotes) });
                        }

                        var newNode = new ReaderChapterNode
                        {
                            ChapterId = targetChapter.Id,
                            Title = targetChapter.Title,
                            Content = extractedContent,
                            Paragraphs = new ObservableCollection<ParagraphUiModel>(paragraphsUi)
                        };
                        ReadingStream.Add(newNode);
                    }
                    else
                    {
                        // ==================== 翻页流模式 ====================
                        if (clearStream) PagedStream.Clear();

                        int charsPerPage = 550; // 假设每页容纳 550 个字

                        // 第 1 步：先按照你定义的标准领域模型 (ReaderPageNode) 进行切页，保证底层纯洁性
                        var domainPages = new List<ReaderPageNode>();
                        int currentPageCharCount = 0;
                        string currentPageContent = "";
                        int pageIndex = 1;

                        foreach (var p in rawParagraphs)
                        {
                            currentPageContent += p + "\n";
                            currentPageCharCount += p.Length;

                            if (currentPageCharCount >= charsPerPage)
                            {
                                domainPages.Add(new ReaderPageNode
                                {
                                    ChapterId = targetChapter.Id,
                                    ChapterTitle = targetChapter.Title,
                                    Content = currentPageContent.TrimEnd('\n'),
                                    PageIndex = pageIndex
                                });
                                pageIndex++;
                                currentPageContent = "";
                                currentPageCharCount = 0;
                            }
                        }

                        // 收尾最后一页
                        if (!string.IsNullOrWhiteSpace(currentPageContent))
                        {
                            domainPages.Add(new ReaderPageNode
                            {
                                ChapterId = targetChapter.Id,
                                ChapterTitle = targetChapter.Title,
                                Content = currentPageContent.TrimEnd('\n'),
                                PageIndex = pageIndex
                            });
                        }

                        // 回填总页数
                        int totalPages = domainPages.Count;
                        foreach (var dp in domainPages) dp.TotalPagesInChapter = totalPages;

                        // 第 2 步：将标准领域模型转化为支持多色高亮的 UI 模型 (ReaderPageUiModel)
                        foreach (var dp in domainPages)
                        {
                            var uiPage = new ReaderPageUiModel
                            {
                                ChapterId = dp.ChapterId,
                                Title = dp.ChapterTitle,
                                PageIndicator = $"{dp.PageIndex} / {dp.TotalPagesInChapter}"
                            };

                            var pageRawParagraphs = dp.Content.Split('\n');
                            foreach (var prp in pageRawParagraphs)
                            {
                                uiPage.Paragraphs.Add(new ParagraphUiModel
                                {
                                    RawText = prp,
                                    FormattedText = BuildFormattedParagraph(prp, chapterNotes)
                                });
                            }

                            PagedStream.Add(uiPage);
                        }
                    }

                    _lastLoadedSortOrder = targetChapter.SortOrder;
                });

                CurrentBook.ProgressLocator = $"{{\"chapterId\": \"{targetChapter.Id}\"}}";
                await _bookRepository.UpdateBookProgressAsync(CurrentBook.Id, CurrentBook.ProgressLocator);
                if (!clearStream) await Task.Delay(300);
            }
            catch (Exception ex) { LocalLogger.LogError($"加载章节失败: {ex.Message}"); }
            finally { if (clearStream) IsLoading = false; else IsLoadingNext = false; }
        }

        private async Task RefreshHighlightingAsync()
        {
            if (CurrentBook == null) return;
            var allNotes = await _noteRepository.GetNotesByBookIdAsync(CurrentBook.Id);

            MainThread.BeginInvokeOnMainThread(() =>
            {
                foreach (var node in ReadingStream)
                {
                    var chapterNotes = allNotes.Where(n => n.ChapterId == node.ChapterId && !n.IsDeleted).ToList();
                    foreach (var p in node.Paragraphs) p.FormattedText = BuildFormattedParagraph(p.RawText, chapterNotes);
                }
                foreach (var page in PagedStream)
                {
                    var chapterNotes = allNotes.Where(n => n.ChapterId == page.ChapterId && !n.IsDeleted).ToList();
                    foreach (var p in page.Paragraphs) p.FormattedText = BuildFormattedParagraph(p.RawText, chapterNotes);
                }
            });
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
            if (CurrentBook == null || IsLoading) return;

            var firstChapterId = IsScrollMode && ReadingStream.Any() ? ReadingStream.First().ChapterId :
                                 (!IsScrollMode && PagedStream.Any() ? PagedStream.First().ChapterId : Guid.Empty);

            var firstChapter = CurrentBook.Chapters.FirstOrDefault(c => c.Id == firstChapterId);
            if (firstChapter == null) return;
            var prevChapter = CurrentBook.Chapters.Where(c => c.SortOrder < firstChapter.SortOrder).OrderByDescending(c => c.SortOrder).FirstOrDefault();
            if (prevChapter != null) await LoadChapterIntoStreamAsync(prevChapter, clearStream: true);
        }

        [RelayCommand]
        private async Task NextChapterAsync()
        {
            if (CurrentBook == null || IsLoading) return;

            var lastChapterId = IsScrollMode && ReadingStream.Any() ? ReadingStream.Last().ChapterId :
                                (!IsScrollMode && PagedStream.Any() ? PagedStream.Last().ChapterId : Guid.Empty);

            var lastChapter = CurrentBook.Chapters.FirstOrDefault(c => c.Id == lastChapterId);
            if (lastChapter == null) return;
            var nextChapter = CurrentBook.Chapters.Where(c => c.SortOrder > lastChapter.SortOrder).OrderBy(c => c.SortOrder).FirstOrDefault();
            if (nextChapter != null) await LoadChapterIntoStreamAsync(nextChapter, clearStream: true);
        }

        [RelayCommand] private void ProcessTap() => HandleMenuToggleRequest();

        private void HandleMenuToggleRequest()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (IsNoteOverlayVisible) { CloseAllMenus(); return; }
                if ((DateTime.Now - _lastToggleTime).TotalMilliseconds < 350) return;
                _lastToggleTime = DateTime.Now;
                IsMenuVisible = !IsMenuVisible;
            });
        }

        private async Task SaveHighlightSilentlyAsync(string colorHex, string selectedText)
        {
            if (CurrentBook == null || CurrentChapter == null || string.IsNullOrWhiteSpace(selectedText)) return;
            var note = new BookNote { Id = Guid.NewGuid(), BookId = CurrentBook.Id, ChapterId = CurrentChapter.Id, BookTitle = CurrentBook.Title, SelectedText = selectedText, UserComment = string.Empty, Color = colorHex, CreatedAt = DateTime.UtcNow, IsDeleted = false };
            await _noteRepository.AddAsync(note);
            await RefreshHighlightingAsync();
        }

        [RelayCommand] private void CloseAllMenus() => IsNoteOverlayVisible = false;

        [RelayCommand]
        private async Task SaveNoteWithCommentAsync()
        {
            if (CurrentBook == null || CurrentChapter == null || string.IsNullOrWhiteSpace(ExtractTargetParagraph)) return;
            var note = new BookNote { Id = Guid.NewGuid(), BookId = CurrentBook.Id, ChapterId = CurrentChapter.Id, BookTitle = CurrentBook.Title, SelectedText = ExtractTargetParagraph, UserComment = UserNoteInput, Color = "#FFF9C4", CreatedAt = DateTime.UtcNow, IsDeleted = false };
            await _noteRepository.AddAsync(note);
            await RefreshHighlightingAsync();
            CloseAllMenus();
        }

        [RelayCommand]
        private async Task GoBackAsync()
        {
            var duration = (int)(DateTime.UtcNow - _sessionStartTime).TotalSeconds;
            if (duration > 10) await _recordRepository.AddDurationAsync(DateTime.UtcNow, duration);
            await Shell.Current.GoToAsync("..");
        }

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
    }
}