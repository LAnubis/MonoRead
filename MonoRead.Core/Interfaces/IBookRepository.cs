using MonoRead.Core.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace MonoRead.Core.Interfaces
{
    public interface IBookRepository
    {
        // 一次性保存书籍和所有章节目录的原子操作
        Task SaveBookWithChaptersAsync(Book book, List<BookChapter> chapters);
    }
}
