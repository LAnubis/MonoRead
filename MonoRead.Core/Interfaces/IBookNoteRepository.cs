using MonoRead.Core.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace MonoRead.Core.Interfaces
{
    public interface IBookNoteRepository
    {
        Task AddAsync(BookNote note);
        Task<List<BookNote>> GetAllNotDeletedAsync();
        // 【新增】：按特定书籍查询所有非删除状态的笔记
        Task<List<BookNote>> GetNotesByBookIdAsync(Guid bookId);
        // 在现有接口中补充：
        Task SoftDeleteNotesAsync(IEnumerable<Guid> noteIds);
    }
}
