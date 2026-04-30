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

        // 【架构强化】：严格的正则屏障。限定标题长度，防止将正文中的对话误判为章节。
        private static readonly Regex ChapterRegex = new Regex(
            @"^\s*(.*?\s+)?(第[零一二三四五六七八九十百千万0-9]+[章回节卷集幕计]|[\d]+[\.\s])[^\n]{0,40}$",
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

            // 【核心修复】：三个绝对游标，彻底消灭 ReadLine 带来的位置漂移
            long absoluteCharIndex = 0;     // 全局绝对字符计数器
            long currentLineStartIndex = 0; // 当前正在读取的这一行的起始游标
            long chapterStartIndex = 0;     // 章节起始游标

            string currentTitle = "前言";
            var lineBuilder = new System.Text.StringBuilder();

            // 使用 StreamReader 直接读取 char[]，精准掌握每一个字符
            using (var reader = new StreamReader(sandboxFilePath, System.Text.Encoding.UTF8))
            {
                char[] buffer = new char[8192];
                int charsRead;

                while ((charsRead = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    for (int i = 0; i < charsRead; i++)
                    {
                        char c = buffer[i];

                        // 遇到换行，说明一行拼接完毕，进行业务判定
                        if (c == '\n')
                        {
                            string line = lineBuilder.ToString();
                            lineBuilder.Clear();

                            // 防伪判定：短于40字符，且符合正则，才被承认为真实章节
                            if (line.Length < 40 && ChapterRegex.IsMatch(line))
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
                                // 新章节的起始位置 = 当前被确认为章节标题的这一行的起点
                                chapterStartIndex = currentLineStartIndex;
                            }

                            // \n 之后的下一个字符，就是新的一行的起点
                            currentLineStartIndex = absoluteCharIndex + 1;
                        }
                        else if (c != '\r')
                        {
                            // 剔除 \r 的干扰，只将有效文字放入检测器
                            lineBuilder.Append(c);
                        }

                        // 【灵魂逻辑】：无论是不是 \r 或 \n，只要流里过了一个字符，绝对游标必须 +1
                        absoluteCharIndex++;
                    }
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

            await _bookRepository.SaveBookWithChaptersAsync(book, chapters);
            return book;
        }
    }
}
