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

            long currentCharacterIndex = 0; // 记录当前游标在文件中的绝对字符位置
            long chapterStartIndex = 0;     // 当前这一章的起始字符位置
            string currentTitle = "前言";

            using (var reader = new StreamReader(sandboxFilePath))
            {
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (!string.IsNullOrWhiteSpace(line) && ChapterRegex.IsMatch(line))
                    {
                        // 发现新章节！将上一章（或前言）的目录和定位索引存入列表
                        chapters.Add(new BookChapter
                        {
                            Id = Guid.NewGuid(),
                            BookId = book.Id,
                            Title = currentTitle,
                            SortOrder = sortOrder++,
                            // 核心：记录该章节在文件中的绝对索引
                            StartLocator = $"{{\"position\": {chapterStartIndex}}}"
                        });

                        // 刷新当前章节数据
                        currentTitle = line.Trim();
                        chapterStartIndex = currentCharacterIndex;
                    }

                    // 累加游标。因为 ReadLineAsync 会吞掉换行符，我们手动加上 \r\n 的长度(2)，以保证后期切片读取时游标精准
                    currentCharacterIndex += line.Length + 2;
                }

                // EOF 结算：将最后一章压入
                chapters.Add(new BookChapter
                {
                    Id = Guid.NewGuid(),
                    BookId = book.Id,
                    Title = currentTitle,
                    SortOrder = sortOrder++,
                    StartLocator = $"{{\"position\": {chapterStartIndex}}}"
                });
            }

            // 一次性无阻塞入库
            await _bookRepository.SaveBookWithChaptersAsync(book, chapters);

            return book;
        }
    }
}
