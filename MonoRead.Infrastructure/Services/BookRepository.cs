using Microsoft.EntityFrameworkCore;
using MonoRead.Core.Entities;
using MonoRead.Core.Interfaces;
using MonoRead.Infrastructure;

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

        public async Task<string?> PermanentlyDeleteBookAsync(Guid bookId, bool destroyNotes)
        {
            _context.ChangeTracker.Clear(); // 【核心修复】
            var book = await _context.Books.IgnoreQueryFilters().FirstOrDefaultAsync(b => b.Id == bookId);
            if (book == null) return null;

            string physicalFilePath = book.FilePath;
            var allNotes = await _context.BookNotes.IgnoreQueryFilters().Where(n => n.BookId == bookId).ToListAsync();

            foreach (var note in allNotes)
            {
                if (destroyNotes) _context.BookNotes.Remove(note);
                else
                {
                    note.IsOrphan = true;
                    note.BookId = null;
                    note.ChapterId = null;
                }
            }

            _context.Books.Remove(book);
            await _context.SaveChangesAsync();
            return physicalFilePath;
        }
    }
}