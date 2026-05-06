using Microsoft.EntityFrameworkCore;
using MonoRead.Core.Entities;
using MonoRead.Core.Interfaces;
using MonoRead.Infrastructure;
using MonoRead.Infrastructure.Logging;

namespace MonoRead.Infrastructure.services
{
    public class BookRepository : IBookRepository
    {
        private readonly AppDbContext _context;

        public BookRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<List<Book>> GetAllBooksAsync()
        {
            // 【核心修复】：全局拉取书籍时，利用 EF Core 的 Include 贪婪加载轻量级的章节目录树。
            // 彻底解决书架、最近阅读页面冷启动时 Chapters 为 null 导致的“未解析”问题。
            return await _context.Books
                                 .Include(b => b.Chapters)
                                 .ToListAsync();
        }

        public async Task<Book?> GetBookWithChaptersAsync(Guid bookId)
        {
            return await _context.Books.Include(b => b.Chapters).FirstOrDefaultAsync(b => b.Id == bookId);
        }

        public async Task UpdateAsync(Book book)
        {
            _context.ChangeTracker.Clear(); // 强制清理缓存
            _context.Books.Update(book);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateBookProgressAsync(Guid bookId, string locator)
        {
            var book = await _context.Books.FindAsync(bookId);
            if (book != null)
            {
                book.ProgressLocator = locator;
                await _context.SaveChangesAsync();
            }
        }

        public async Task SaveBookWithChaptersAsync(Book book, IEnumerable<BookChapter> chapters)
        {
            if (book == null) throw new ArgumentNullException(nameof(book));
            book.Chapters = chapters.ToList();
            _context.Books.Add(book);
            await _context.SaveChangesAsync();
        }

        public async Task<bool> HasActiveNotesAsync(Guid bookId)
        {
            return await _context.BookNotes.AnyAsync(n => n.BookId == bookId && !n.IsDeleted);
        }

        public async Task ArchiveBookSafelyAsync(Guid bookId, bool archiveNotes)
        {
            _context.ChangeTracker.Clear(); // 【核心修复】：防止上下文冲突
            var book = await _context.Books.FindAsync(bookId);
            if (book == null) return;

            book.IsDeleted = true;

            var relatedNotes = await _context.BookNotes.Where(n => n.BookId == bookId && !n.IsDeleted).ToListAsync();
            foreach (var note in relatedNotes)
            {
                if (archiveNotes) note.IsDeleted = true;
                else note.IsOrphan = true;
            }
            await _context.SaveChangesAsync();
        }

        public async Task<List<Book>> GetDeletedBooksAsync()
        {
            return await _context.Books.IgnoreQueryFilters().Where(b => b.IsDeleted).ToListAsync();
        }

        public async Task RestoreBookAsync(Guid bookId)
        {
            _context.ChangeTracker.Clear(); // 【核心修复】
            var book = await _context.Books.IgnoreQueryFilters().FirstOrDefaultAsync(b => b.Id == bookId);
            if (book != null)
            {
                book.IsDeleted = false;
                var orphanNotes = await _context.BookNotes.IgnoreQueryFilters()
                    .Where(n => n.BookId == bookId && n.IsOrphan && !n.IsDeleted).ToListAsync();

                foreach (var note in orphanNotes) note.IsOrphan = false;
                await _context.SaveChangesAsync();
            }
        }

        public async Task PermanentlyDeleteBookAsync(Guid bookId, bool destroyNotes)
        {
            // 1. 找到这本要被销毁的书
            var book = await _context.Books.FindAsync(bookId);
            if (book == null) return;

            // 2. 【核心修复 A】：找出所有的笔记，妥善安置它们
            var notes = await _context.BookNotes.Where(n => n.BookId == bookId).ToListAsync();
            if (notes.Any())
            {
                if (destroyNotes)
                {
                    // 用户选择玉石俱焚：直接连笔记一起物理删除
                    _context.BookNotes.RemoveRange(notes);
                }
                else
                {
                    // 用户选择保留笔记：必须同时解除 BookId 和 ChapterId 的外键约束！
                    foreach (var note in notes)
                    {
                        note.BookId = null;
                        note.ChapterId = null; // 👈 极其关键：因为马上连章节也要被删了！
                        note.IsOrphan = true;
                    }
                    _context.BookNotes.UpdateRange(notes);
                }
            }

            // 3. 【核心修复 B】：主动查出并剿灭所有关联的章节，绝不依赖 EF Core 脆弱的自动级联
            var chapters = await _context.BookChapters.Where(c => c.BookId == bookId).ToListAsync();
            if (chapters.Any())
            {
                _context.BookChapters.RemoveRange(chapters);
            }

            // 4. 清理完毕，物理删除书籍本体
            _context.Books.Remove(book);

            // 5. 提交事务（如果有任何步骤失败，会自动回滚）
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                LocalLogger.LogError($"永久删除书籍时发生数据库异常: {ex.Message} \n内部异常: {ex.InnerException?.Message}");
                throw; // 抛出异常让上层 ViewModel 处理
            }
        }
    }
}