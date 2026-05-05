using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MonoRead.Core.Entities;
using MonoRead.Core.Interfaces;
using MonoRead.Infrastructure.Logging;
using System.Collections.ObjectModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using System.Linq;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace MonoRead.App.ViewModels
{
    public record MenuToggleRequestedMessage();
    public record TextSelectionStartedMessage();
    public record TextSelectedMessage(string SelectedText);

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

    [QueryProperty(nameof(BookIdString), "BookId")]
    public partial class ReaderViewModel : ObservableObject
    {
        private readonly IBookRepository _bookRepository;
        private readonly IBookNoteRepository _noteRepository;

        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private bool _isLoadingNext;
        [ObservableProperty] private string _bookIdString = string.Empty;
        [ObservableProperty] private Book? _currentBook;
        [ObservableProperty] private BookChapter? _currentChapter;
        [ObservableProperty] private string _chapterTitle = "加载中...";

        [ObservableProperty] private ObservableCollection<ReaderChapterNode> _readingStream = new();
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

        // ==================== 极简操作台状态机 ====================
        [ObservableProperty] private bool _isFloatingMenuVisible = false; // 悬浮菜单
        [ObservableProperty] private bool _isNoteOverlayVisible = false;  // 写想法工作台
        [ObservableProperty] private string _extractTargetParagraph = string.Empty;
        [ObservableProperty] private string _userNoteInput = string.Empty;

        private DateTime _lastToggleTime = DateTime.MinValue;

        public ReaderViewModel(IBookRepository bookRepository, IBookNoteRepository noteRepository)
        {
            _bookRepository = bookRepository;
            _noteRepository = noteRepository;

            WeakReferenceMessenger.Default.Register<MenuToggleRequestedMessage>(this, (r, m) => HandleMenuToggleRequest());
            WeakReferenceMessenger.Default.Register<TextSelectionStartedMessage>(this, (r, m) => MainThread.BeginInvokeOnMainThread(() => IsMenuVisible = false));
            WeakReferenceMessenger.Default.Register<TextSelectedMessage>(this, (r, m) =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (!string.IsNullOrWhiteSpace(m.SelectedText))
                    {
                        ExtractTargetParagraph = m.SelectedText;
                        IsMenuVisible = false;
                        IsFloatingMenuVisible = true; // 拦截系统选词，弹出极简菜单
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

        private FormattedString BuildFormattedParagraph(string paragraphText, List<BookNote> chapterNotes)
        {
            var formattedString = new FormattedString();

            var matchedNotes = chapterNotes
                .Where(n => !string.IsNullOrWhiteSpace(n.SelectedText) && paragraphText.Contains(n.SelectedText))
                .ToList();

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

                formattedString.Spans.Add(new Span
                {
                    Text = occurrence.Note.SelectedText,
                    BackgroundColor = Color.FromArgb(colorHex)
                });

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

                var paragraphsUi = new ObservableCollection<ParagraphUiModel>();
                foreach (var text in rawParagraphs)
                {
                    paragraphsUi.Add(new ParagraphUiModel
                    {
                        RawText = text,
                        FormattedText = BuildFormattedParagraph(text, chapterNotes)
                    });
                }

                var newNode = new ReaderChapterNode
                {
                    ChapterId = targetChapter.Id,
                    Title = targetChapter.Title,
                    Content = extractedContent,
                    Paragraphs = paragraphsUi
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

        private async Task RefreshHighlightingAsync()
        {
            if (CurrentBook == null) return;
            var allNotes = await _noteRepository.GetNotesByBookIdAsync(CurrentBook.Id);

            MainThread.BeginInvokeOnMainThread(() =>
            {
                foreach (var node in ReadingStream)
                {
                    var chapterNotes = allNotes.Where(n => n.ChapterId == node.ChapterId && !n.IsDeleted).ToList();
                    foreach (var p in node.Paragraphs)
                    {
                        p.FormattedText = BuildFormattedParagraph(p.RawText, chapterNotes);
                    }
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

        [RelayCommand]
        private void ProcessTap() => HandleMenuToggleRequest();

        private void HandleMenuToggleRequest()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                // 如果有任何弹出菜单开着，优先把它关掉，而不呼出主菜单
                if (IsNoteOverlayVisible || IsFloatingMenuVisible)
                {
                    CloseAllMenus();
                    return;
                }
                if ((DateTime.Now - _lastToggleTime).TotalMilliseconds < 350) return;

                _lastToggleTime = DateTime.Now;
                IsMenuVisible = !IsMenuVisible;
            });
        }

        // ====================================================================
        // 【核心操作分流】：悬浮菜单的各类交互
        // ====================================================================

        // 1. 双击段落：提取文字并呼出【极简悬浮菜单】
        [RelayCommand]
        private void OpenFloatingMenu(string rawParagraphText)
        {
            ExtractTargetParagraph = rawParagraphText;
            IsMenuVisible = false;
            IsFloatingMenuVisible = true;
        }

        // 2. 悬浮菜单：点击了颜色圆点（默默存入，刷新高亮）
        [RelayCommand]
        private async Task SaveHighlightSilentlyAsync(string colorHex)
        {
            if (CurrentBook == null || CurrentChapter == null) return;
            if (string.IsNullOrWhiteSpace(ExtractTargetParagraph)) return;

            var note = new BookNote
            {
                Id = Guid.NewGuid(),
                BookId = CurrentBook.Id,
                ChapterId = CurrentChapter.Id,
                BookTitle = CurrentBook.Title,
                SelectedText = ExtractTargetParagraph,
                UserComment = string.Empty,  // 【核心】：想法为空
                Color = colorHex,            // 【核心】：记录选中的莫兰迪色
                CreatedAt = DateTime.UtcNow,
                IsDeleted = false
            };

            await _noteRepository.AddAsync(note);
            await RefreshHighlightingAsync();

            CloseAllMenus(); // 操作完成，深藏功与名
        }

        // 3. 悬浮菜单：点击了“写想法” -> 转入工作台
        [RelayCommand]
        private void OpenNoteWorkbench()
        {
            IsFloatingMenuVisible = false;
            UserNoteInput = string.Empty;
            IsNoteOverlayVisible = true; // 展开深度编辑台
        }

        // 4. 悬浮菜单：点击了“复制”
        [RelayCommand]
        private async Task CopyTextAsync()
        {
            if (!string.IsNullOrWhiteSpace(ExtractTargetParagraph))
            {
                await Clipboard.Default.SetTextAsync(ExtractTargetParagraph);
            }
            CloseAllMenus();
        }

        // 5. 工作台：保存带想法的笔记
        [RelayCommand]
        private async Task SaveNoteWithCommentAsync()
        {
            if (CurrentBook == null || CurrentChapter == null) return;
            if (string.IsNullOrWhiteSpace(ExtractTargetParagraph)) return;

            var note = new BookNote
            {
                Id = Guid.NewGuid(),
                BookId = CurrentBook.Id,
                ChapterId = CurrentChapter.Id,
                BookTitle = CurrentBook.Title,
                SelectedText = ExtractTargetParagraph,
                UserComment = UserNoteInput,
                Color = "#FFF9C4", // 手动写想法时，默认给一个浅黄色背景
                CreatedAt = DateTime.UtcNow,
                IsDeleted = false
            };

            await _noteRepository.AddAsync(note);
            await RefreshHighlightingAsync();
            CloseAllMenus();
        }

        [RelayCommand]
        private void CloseAllMenus()
        {
            IsFloatingMenuVisible = false;
            IsNoteOverlayVisible = false;
        }

        // ==================== 界面控制 ====================
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
    }
}