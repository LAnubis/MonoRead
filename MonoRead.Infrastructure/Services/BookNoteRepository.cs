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
    }
}
