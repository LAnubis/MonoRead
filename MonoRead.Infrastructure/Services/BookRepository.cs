using MonoRead.Core.Entities;
using MonoRead.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;

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
    }
}
