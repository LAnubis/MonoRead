using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonoRead.Core.Entities;
using MonoRead.Core.Interfaces;
using System.Diagnostics;

namespace MonoRead.App.ViewModels;

[QueryProperty(nameof(BookIdString), "BookId")]
public partial class ReaderViewModel : ObservableObject
{
    private readonly IBookRepository _bookRepository;
    // 1. 在顶部加入一个 Loading 状态
    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _bookIdString = string.Empty;

    [ObservableProperty]
    private Book? _currentBook;

    // 记录当前正在阅读的章节
    [ObservableProperty]
    private BookChapter? _currentChapter;

    [ObservableProperty]
    private string _chapterTitle = "加载中...";

    [ObservableProperty]
    private string _pageContent = "正在排版正文...";

    // 控制“上一章/下一章”按钮是否可用
    [ObservableProperty]
    private bool _canGoPrevious;

    [ObservableProperty]
    private bool _canGoNext;

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
                // TODO: 未来这里会从 CurrentBook.ProgressLocator 里读取上次看到哪一章，现在先默认第一章
                var targetChapter = CurrentBook.Chapters.OrderBy(c => c.SortOrder).First();
                await LoadChapterContentAsync(targetChapter);
            }
        }
        catch (Exception ex)
        {
            PageContent = $"加载失败: {ex.Message}";
        }
    }

    // 【升级版】流式读取引擎，同时更新按钮状态
    // 2. 修改 LoadChapterContentAsync，加入防并发锁
    // 【终极排版引擎】加入 Task.Run 线程隔离，彻底解放 UI 渲染！
    private async Task LoadChapterContentAsync(BookChapter targetChapter)
    {
        if (IsLoading) return; // 防连点锁
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
            // 告诉用户正在加载，此时 UI 瞬间响应
            PageContent = "正在极速排版中，请稍候...";

            CanGoPrevious = CurrentBook.Chapters.Any(c => c.SortOrder < targetChapter.SortOrder);
            CanGoNext = CurrentBook.Chapters.Any(c => c.SortOrder > targetChapter.SortOrder);

            // 【核心绝杀】：用 Task.Run 开启一条后台专属通道，把成千上万次的循环计算全扔进去
            string extractedContent = await Task.Run(async () =>
            {
                // 1. 解析起始索引
                long startCharIndex = 0;
                if (!string.IsNullOrWhiteSpace(targetChapter.StartLocator) && targetChapter.StartLocator != "{}")
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(targetChapter.StartLocator);
                    startCharIndex = doc.RootElement.GetProperty("position").GetInt64();
                }

                // 2. 解析结束索引
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

                // 3. 高速跳跃（这里就算循环一万次，也绝对不会卡顿 UI）
                if (startCharIndex > 0)
                {
                    char[] throwawayBuffer = new char[8192]; // 加大跳跃步长，提升一倍速度
                    long remainingToSkip = startCharIndex;
                    while (remainingToSkip > 0)
                    {
                        int toRead = (int)Math.Min(throwawayBuffer.Length, remainingToSkip);
                        int readCount = await reader.ReadAsync(throwawayBuffer, 0, toRead);
                        if (readCount == 0) break;
                        remainingToSkip -= readCount;
                    }
                }

                // 4. 精准截取本章正文
                char[] contentBuffer = new char[lengthToRead];
                int charsRead = await reader.ReadAsync(contentBuffer, 0, lengthToRead);
                return new string(contentBuffer, 0, charsRead);
            });

            // 拿到结果后，强制切回主线程去渲染界面
            MainThread.BeginInvokeOnMainThread(() =>
            {
                PageContent = string.IsNullOrWhiteSpace(extractedContent) ? "（解析内容为空）" : extractedContent;
            });
        }
        catch (Exception ex)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                PageContent = $"读取正文失败：\n{ex.Message}";
            });
        }
        finally
        {
            // 无论成功失败，必须解锁
            IsLoading = false;
        }
    }

    // 3. 在上一章和下一章命令前，也加上安全校验
    [RelayCommand]
    private async Task PreviousChapterAsync()
    {
        if (CurrentBook == null || CurrentChapter == null || IsLoading) return;
        var prevChapter = CurrentBook.Chapters.Where(c => c.SortOrder < CurrentChapter.SortOrder).OrderByDescending(c => c.SortOrder).FirstOrDefault();
        if (prevChapter != null)
        {
            await LoadChapterContentAsync(prevChapter);
        }
    }

    [RelayCommand]
    private async Task NextChapterAsync()
    {
        if (CurrentBook == null || CurrentChapter == null || IsLoading) return;
        var nextChapter = CurrentBook.Chapters.Where(c => c.SortOrder > CurrentChapter.SortOrder).OrderBy(c => c.SortOrder).FirstOrDefault();
        if (nextChapter != null)
        {
            await LoadChapterContentAsync(nextChapter);
        }
    }

    [RelayCommand]
    private async Task GoBackAsync()
    {
        await Shell.Current.GoToAsync("..");
    }
}