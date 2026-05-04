using MonoRead.Core.Entities;

namespace MonoRead.Core.Interfaces
{
    public interface IBookNoteRepository
    {
        Task AddAsync(BookNote note);
        Task<List<BookNote>> GetAllNotDeletedAsync();
        Task<List<BookNote>> GetNotesByBookIdAsync(Guid bookId);
        Task SoftDeleteNotesAsync(IEnumerable<Guid> noteIds);

        // 获取所有被软删除的笔记
        Task<List<BookNote>> GetDeletedNotesAsync();

        // 【核心新增】：获取所有未被删除，但已失去原书关联的未分类(孤儿)笔记
        Task<List<BookNote>> GetOrphanNotesAsync();

        Task RestoreNoteAsync(Guid noteId);
        Task PermanentlyDeleteNoteAsync(Guid noteId);
    }
}