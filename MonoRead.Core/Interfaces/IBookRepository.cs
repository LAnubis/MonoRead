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
        // 【新增 1】：前置探针：查询该书是否有正常状态的笔记
        Task<bool> HasActiveNotesAsync(Guid bookId);

        // 【新增 2】：复合安全删除事务
        // archiveNotes = true 代表连带笔记一起软删除（进回收站）
        // archiveNotes = false 代表保留笔记（变为孤儿/未分类）
        Task ArchiveBookSafelyAsync(Guid bookId, bool archiveNotes);
    }
}
