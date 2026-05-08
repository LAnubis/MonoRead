using Microsoft.EntityFrameworkCore;
using MonoRead.Core.Entities;
using MonoRead.Core.Interfaces;

namespace MonoRead.Infrastructure.Services
{
    public class BookSourceRepository : IBookSourceRepository
    {
        // 【核心修复】：去掉 Factory，直接使用 AppDbContext
        private readonly AppDbContext _context;

        public BookSourceRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<List<BookSource>> GetAllAsync()
        {
            // 直接使用 _context 进行查询
            return await _context.BookSources.ToListAsync();
        }

        public async Task AddAsync(BookSource source)
        {
            _context.BookSources.Add(source);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(BookSource source)
        {
            _context.BookSources.Update(source);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(BookSource source)
        {
            _context.BookSources.Remove(source);
            await _context.SaveChangesAsync();
        }
    }
}