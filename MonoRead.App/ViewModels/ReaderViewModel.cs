using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonoRead.Core.Entities;
using MonoRead.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace MonoRead.App.ViewModels
{
    // 接收来自路由传参的 BookId
    [QueryProperty(nameof(BookId), "BookId")]
    public partial class ReaderViewModel : ObservableObject
    {
        private readonly IBookRepository _bookRepository;

        [ObservableProperty]
        private Guid _bookId;

        [ObservableProperty]
        private Book? _currentBook;

        [ObservableProperty]
        private string _chapterTitle = "加载中...";

        [ObservableProperty]
        private string _pageContent = "正在排版正文...";

        public ReaderViewModel(IBookRepository bookRepository)
        {
            _bookRepository = bookRepository;
        }

        // 当 BookId 被路由赋值时，自动触发此方法
        partial void OnBookIdChanged(Guid value)
        {
            LoadBookDataAsync(value);
        }

        private async void LoadBookDataAsync(Guid bookId)
        {
            try
            {
                CurrentBook = await _bookRepository.GetBookWithChaptersAsync(bookId);

                if (CurrentBook != null && CurrentBook.Chapters.Any())
                {
                    // 获取第一章
                    var firstChapter = CurrentBook.Chapters.OrderBy(c => c.SortOrder).First();

                    // 开始硬核流式读取正文！
                    await LoadChapterContentAsync(firstChapter);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"加载书籍失败: {ex.Message}");
                PageContent = "书籍加载失败。";
            }
        }

        // 【核心算法】根据 JSON 索引，去硬盘里精准截断文本
        private async Task LoadChapterContentAsync(BookChapter currentChapter)
        {
            try
            {
                ChapterTitle = currentChapter.Title;
                PageContent = "正在从硬盘深处挖掘正文...";

                // 1. 解析当前章节的起始字符索引
                long startCharIndex = 0;
                if (!string.IsNullOrWhiteSpace(currentChapter.StartLocator) && currentChapter.StartLocator != "{}")
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(currentChapter.StartLocator);
                    startCharIndex = doc.RootElement.GetProperty("position").GetInt64();
                }

                // 2. 找到下一章的位置，从而计算出本章到底有多少个字
                var nextChapter = CurrentBook.Chapters.FirstOrDefault(c => c.SortOrder == currentChapter.SortOrder + 1);
                long endCharIndex = long.MaxValue; // 默认读到文件末尾
                if (nextChapter != null && !string.IsNullOrWhiteSpace(nextChapter.StartLocator) && nextChapter.StartLocator != "{}")
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(nextChapter.StartLocator);
                    endCharIndex = doc.RootElement.GetProperty("position").GetInt64();
                }

                // 计算本章长度 (防止有些超大章节撑爆内存，强制限制单章最多加载 20000 字)
                int lengthToRead = endCharIndex == long.MaxValue ? 20000 : (int)(endCharIndex - startCharIndex);
                if (lengthToRead > 20000) lengthToRead = 20000;

                // 3. 打开文件流 (防 OOM 绝杀)
                using var reader = new StreamReader(CurrentBook.FilePath);

                // 高速跳过前面的无关字符，直达本章起点
                if (startCharIndex > 0)
                {
                    char[] throwawayBuffer = new char[4096];
                    long remainingToSkip = startCharIndex;
                    while (remainingToSkip > 0)
                    {
                        int toRead = (int)Math.Min(throwawayBuffer.Length, remainingToSkip);
                        int readCount = await reader.ReadAsync(throwawayBuffer, 0, toRead);
                        if (readCount == 0) break; // 到底了
                        remainingToSkip -= readCount;
                    }
                }

                // 4. 精准读取本章的正文字符！
                char[] contentBuffer = new char[lengthToRead];
                int charsRead = await reader.ReadAsync(contentBuffer, 0, lengthToRead);

                // 将字符转化为字符串并渲染到 UI
                PageContent = new string(contentBuffer, 0, charsRead);
            }
            catch (Exception ex)
            {
                PageContent = $"解析正文失败：{ex.Message}\n请检查文件是否损坏。";
            }
        }

        [RelayCommand]
        private async Task GoBackAsync()
        {
            // 退出阅读器，返回书架
            await Shell.Current.GoToAsync("..");
        }
    }
}
