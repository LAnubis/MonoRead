using MonoRead.Core.Entities;

namespace MonoRead.Core.Interfaces
{
    public interface IBookRepository
    {
        // ==================== 基础操作与阅读引擎支撑 ====================

        Task<List<Book>> GetAllBooksAsync();

        Task<Book?> GetBookWithChaptersAsync(Guid bookId);

        Task UpdateAsync(Book book);

        Task UpdateBookProgressAsync(Guid bookId, string locator);


        // ==================== 书架侧：安全删除与孤儿控制 ====================

        // 前置探针：查询该书是否有正常状态（未被软删）的笔记
        Task<bool> HasActiveNotesAsync(Guid bookId);

        // 复合安全删除事务：archiveNotes 控制是否连带笔记一起移入回收站
        Task ArchiveBookSafelyAsync(Guid bookId, bool archiveNotes);


        // ==================== 回收站侧：恢复与终极物理销毁 ====================

        // 获取所有被软删除的书籍
        Task<List<Book>> GetDeletedBooksAsync();

        // 恢复书籍（复合事务：自动找回并解除原名下孤儿笔记的状态）
        Task RestoreBookAsync(Guid bookId);

        // 彻底物理销毁书籍（复合事务：处理关联笔记，并返回沙盒物理路径供上层清理）
        Task<string?> PermanentlyDeleteBookAsync(Guid bookId, bool destroyNotes);
        // 【核心修复】：补充丢失的解析入库方法
        Task SaveBookWithChaptersAsync(Book book, IEnumerable<BookChapter> chapters);
    }
}