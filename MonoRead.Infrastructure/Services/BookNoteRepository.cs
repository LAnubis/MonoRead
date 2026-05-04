using Microsoft.EntityFrameworkCore;
using MonoRead.Core.Entities;
using MonoRead.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;

namespace MonoRead.Infrastructure.Services
{
    public class BookNoteRepository : IBookNoteRepository
    {
        private readonly AppDbContext _context;

        public BookNoteRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task AddAsync(BookNote note)
        {
            _context.BookNotes.Add(note);
            await _context.SaveChangesAsync();
        }

        public async Task<List<BookNote>> GetAllNotDeletedAsync()
        {
            // 使用 AsNoTracking 优化纯读取性能
            return await _context.BookNotes
                .AsNoTracking()
                .Where(n => !n.IsDeleted)
                .ToListAsync();
        }
        // 【新增实现】：按时间倒序拉取该本书的所有笔记
        public async Task<List<BookNote>> GetNotesByBookIdAsync(Guid bookId)
        {
            return await _context.BookNotes
                .AsNoTracking()
                .Where(n => n.BookId == bookId && !n.IsDeleted)
                .OrderByDescending(n => n.CreatedAt)
                .ToListAsync();
        }
        public async Task SoftDeleteNotesAsync(IEnumerable<Guid> noteIds)
        {
            // 拉取需要删除的实体
            var notesToDelete = await _context.BookNotes
                .Where(n => noteIds.Contains(n.Id))
                .ToListAsync();

            foreach (var note in notesToDelete)
            {
                note.IsDeleted = true;
                // 配合需求文档 4.4 节：设定 DeletedAt 为当前时间，移入回收站
                // note.DeletedAt = DateTime.UtcNow; // 如果你的 BaseEntity 有这个字段请解除注释
            }

            // 统一提交事务，此时 AppDbContext 拦截器会自动更新 UpdatedAt
            await _context.SaveChangesAsync();
        }
    }
}
