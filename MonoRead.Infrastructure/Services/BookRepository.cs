using MonoRead.Core.Entities;
using MonoRead.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.EntityFrameworkCore; // 必须加上这行！

namespace MonoRead.Infrastructure.Services
{
    public class BookRepository : IBookRepository
    {
        private readonly AppDbContext _dbContext;

        public BookRepository(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task SaveBookWithChaptersAsync(Book book, List<BookChapter> chapters)
        {
            _dbContext.Books.Add(book);
            // 使用 Set<T> 可以防止你 AppDbContext 里的 DbSet 名字和这里不一致的问题
            _dbContext.Set<BookChapter>().AddRange(chapters);
            await _dbContext.SaveChangesAsync();
        }
        // 实现查询逻辑，按导入时间倒序排列
        public async Task<List<Book>> GetAllBooksAsync()
        {
            //return await _dbContext.Books
            //    .Where(b => !b.IsDeleted) // 过滤掉进入回收站的书
            //    .OrderByDescending(b => b.ImportDate)
            //    .ToListAsync();

            return await _dbContext.Books
            .AsNoTracking() // 【核心修复】：彻底禁用 EF Core 的内存追踪，强制每次都从 SQLite 物理文件读取最新进度！
            .Include(b => b.Chapters)
            .Where(b => !b.IsDeleted)
            .OrderByDescending(b => b.UpdatedAt) // 顺便利用上一节我们更新的 UpdatedAt，让最近阅读的书自动排在最前面
            .ToListAsync();
        }
        // 实现根据 ID 贪婪加载章节
        public async Task<Book?> GetBookWithChaptersAsync(Guid bookId)
        {
            return await _dbContext.Books
                .Include(b => b.Chapters) // 连带章节目录一起查出来
                .FirstOrDefaultAsync(b => b.Id == bookId && !b.IsDeleted);
        }
       
        public async Task UpdateBookProgressAsync(Guid bookId, string progressLocator)
        {
            // 【核心修复】：绕过复杂的实体图更新，只查出单条记录并局部更新，绝对不会破坏 Chapters 导航属性
            var book = await _dbContext.Books.FirstOrDefaultAsync(b => b.Id == bookId);
            if (book != null)
            {
                book.ProgressLocator = progressLocator;
                book.UpdatedAt = DateTime.UtcNow; // 顺便刷新最后阅读时间
                await _dbContext.SaveChangesAsync();
            }
        }
    }
}
