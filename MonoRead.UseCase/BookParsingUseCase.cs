using MonoRead.Core.Entities;
using MonoRead.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MonoRead.UseCase
{
    public class BookParsingUseCase : IBookParsingUseCase
    {
        private readonly IBookRepository _bookRepository;

        // 匹配主流网络小说章节名
        private static readonly Regex ChapterRegex = new Regex(
            @"^\s*第[零一二三四五六七八九十百千万\d]+[章回节卷集幕计].*",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public BookParsingUseCase(IBookRepository bookRepository)
        {
            _bookRepository = bookRepository;
        }

        public async Task<Book> ParseAndSplitBookAsync(string sandboxFilePath, string fileName, string fileHash)
        {
            // 1. 【核心修复】：动态侦测文件的换行符长度，彻底消灭坐标漂移！
            int newlineLength = 2; // 默认认定为 \r\n
            using (var fs = new FileStream(sandboxFilePath, FileMode.Open, FileAccess.Read))
            {
                byte[] buffer = new byte[1024];
                int bytesRead = fs.Read(buffer, 0, buffer.Length);
                string snippet = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);
                if (snippet.Contains("\r\n")) newlineLength = 2;
                else if (snippet.Contains("\n")) newlineLength = 1;
            }

            var book = new Book
            {
                Id = Guid.NewGuid(),
                Title = Path.GetFileNameWithoutExtension(fileName),
                FileHash = fileHash,
                FilePath = sandboxFilePath,
                CoverImagePath = "default_cover.png",
                ProgressLocator = "{}"
            };

            var chapters = new List<BookChapter>();
            int sortOrder = 0;
            long currentCharacterIndex = 0;
            long chapterStartIndex = 0;
            string currentTitle = "前言";

            using (var reader = new StreamReader(sandboxFilePath))
            {
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (!string.IsNullOrWhiteSpace(line) && ChapterRegex.IsMatch(line))
                    {
                        chapters.Add(new BookChapter
                        {
                            Id = Guid.NewGuid(),
                            BookId = book.Id,
                            Title = currentTitle,
                            SortOrder = sortOrder++,
                            StartLocator = $"{{\"position\": {chapterStartIndex}}}"
                        });

                        currentTitle = line.Trim();
                        chapterStartIndex = currentCharacterIndex;
                    }

                    // 【核心修复】：使用侦测到的精确换行符长度，一字不差！
                    currentCharacterIndex += line.Length + newlineLength;
                }

                chapters.Add(new BookChapter
                {
                    Id = Guid.NewGuid(),
                    BookId = book.Id,
                    Title = currentTitle,
                    SortOrder = sortOrder++,
                    StartLocator = $"{{\"position\": {chapterStartIndex}}}"
                });
            }

            await _bookRepository.SaveBookWithChaptersAsync(book, chapters);
            return book;
        }
    }
}
