using MonoRead.Core.Entities;
using MonoRead.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq; // 【重要新增】：用于查询 Local 缓存
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

// 注意：我保留了您提供的命名空间 Infrastructure.Services
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
                .AsNoTracking() // 【核心修复】：进入阅读页查询海量数据时，绝不让 EF Core 追踪，大幅节省内存并防冲突
                .Include(b => b.Chapters) // 连带章节目录一起查出来
                .FirstOrDefaultAsync(b => b.Id == bookId && !b.IsDeleted);
        }

        public async Task UpdateBookProgressAsync(Guid bookId, string progressLocator)
        {
            // 绕过复杂的实体图更新，只查出单条记录并局部更新，绝对不会破坏 Chapters 导航属性
            var book = await _dbContext.Books.FirstOrDefaultAsync(b => b.Id == bookId);
            if (book != null)
            {
                book.ProgressLocator = progressLocator;
                book.UpdatedAt = DateTime.UtcNow; // 顺便刷新最后阅读时间
                await _dbContext.SaveChangesAsync();
            }
        }

        // 【终极重构：实体更新实现，防追踪冲突装甲】
        public async Task UpdateAsync(Book book)
        {
            // 1. 检查当前 DbContext 内存中，是否已经有一个同 ID 的实体在被监视（占坑）
            var localTrackedEntity = _dbContext.Set<Book>().Local.FirstOrDefault(entry => entry.Id.Equals(book.Id));

            // 2. 如果有人占了坑，直接将它踢出监视列表（剥离状态）
            if (localTrackedEntity != null)
            {
                _dbContext.Entry(localTrackedEntity).State = EntityState.Detached;
            }

            // 3. 安全地将前端传来的 book 实体挂载上去，并标记为已修改
            _dbContext.Entry(book).State = EntityState.Modified;

            // 4. 提交落盘
            await _dbContext.SaveChangesAsync();
        }

        // 【终极重构：实体删除实现，防追踪冲突装甲】
        public async Task DeleteAsync(Book book)
        {
            // 执行同样的清理动作，防止软删除/硬删除时报 Tracking 错误
            var localTrackedEntity = _dbContext.Set<Book>().Local.FirstOrDefault(entry => entry.Id.Equals(book.Id));
            if (localTrackedEntity != null)
            {
                _dbContext.Entry(localTrackedEntity).State = EntityState.Detached;
            }

            _dbContext.Entry(book).State = EntityState.Deleted;
            await _dbContext.SaveChangesAsync();
        }
        public async Task<bool> HasActiveNotesAsync(Guid bookId)
        {
            // 查询是否有关联且未被软删除的笔记
            return await _dbContext.BookNotes
                .AnyAsync(n => n.BookId == bookId && !n.IsDeleted);
        }

        public async Task ArchiveBookSafelyAsync(Guid bookId, bool archiveNotes)
        {
            var book = await _dbContext.Books.FindAsync(bookId);
            if (book == null) return;

            // 1. 书籍软删除
            book.IsDeleted = true;
            // book.DeletedAt = DateTime.UtcNow; // 根据 BaseEntity 是否有此字段选用

            // 2. 获取该书所有未软删的笔记进行状态扭转
            var relatedNotes = await _dbContext.BookNotes
                .Where(n => n.BookId == bookId && !n.IsDeleted)
                .ToListAsync();

            if (relatedNotes.Any())
            {
                foreach (var note in relatedNotes)
                {
                    if (archiveNotes)
                    {
                        // 连带软删除：陪葬进回收站
                        note.IsDeleted = true;
                        // note.DeletedAt = DateTime.UtcNow;
                    }
                    else
                    {
                        // 仅删书籍，笔记保留：打上孤儿（未分类）标记
                        note.IsOrphan = true;
                    }
                }
            }

            // 3. 统一保存：EF Core 会在一个事务中原子性地处理这些变更，确保不会因为异常导致数据断层
            await _dbContext.SaveChangesAsync();
        }
    }
}