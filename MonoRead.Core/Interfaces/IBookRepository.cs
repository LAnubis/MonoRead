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
        // 新增：获取书架上的所有书
        Task<List<Book>> GetAllBooksAsync();
        // 新增：根据 ID 获取书籍及其关联的章节目录
        Task<Book?> GetBookWithChaptersAsync(Guid bookId);
        // 【新增】更新单本书籍状态（用于保存阅读进度）
     
        Task UpdateBookProgressAsync(Guid bookId, string progressLocator);

        // 【核心新增：实体更新契约】
        Task UpdateAsync(Book book);

        // 顺便预留硬删除接口（以备彻底清空回收站时使用）
        Task DeleteAsync(Book book);
    }
}
