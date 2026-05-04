using Microsoft.EntityFrameworkCore;
using MonoRead.Core.Entities;
using MonoRead.Core.Interfaces;
using MonoRead.Infrastructure;

namespace MonoRead.Infrastructure.services
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
            _context.ChangeTracker.Clear();
            _context.BookNotes.Add(note);
            await _context.SaveChangesAsync();
        }

        public async Task<List<BookNote>> GetAllNotDeletedAsync()
        {
            return await _context.BookNotes.AsNoTracking().Where(n => !n.IsDeleted).ToListAsync();
        }

        public async Task<List<BookNote>> GetNotesByBookIdAsync(Guid bookId)
        {
            return await _context.BookNotes.AsNoTracking()
                .Where(n => n.BookId == bookId && !n.IsDeleted)
                .OrderByDescending(n => n.CreatedAt).ToListAsync();
        }

        public async Task SoftDeleteNotesAsync(IEnumerable<Guid> noteIds)
        {
            _context.ChangeTracker.Clear(); // 【核心修复】
            var notesToDelete = await _context.BookNotes.Where(n => noteIds.Contains(n.Id)).ToListAsync();
            foreach (var note in notesToDelete) note.IsDeleted = true;
            await _context.SaveChangesAsync();
        }

        public async Task<List<BookNote>> GetDeletedNotesAsync()
        {
            return await _context.BookNotes.IgnoreQueryFilters().AsNoTracking()
                .Where(n => n.IsDeleted).OrderByDescending(n => n.UpdatedAt).ToListAsync();
        }

        public async Task<List<BookNote>> GetOrphanNotesAsync()
        {
            return await _context.BookNotes.AsNoTracking()
                .Where(n => n.IsOrphan && !n.IsDeleted).OrderByDescending(n => n.UpdatedAt).ToListAsync();
        }

        public async Task RestoreNoteAsync(Guid noteId)
        {
            _context.ChangeTracker.Clear(); // 【核心修复】
            var note = await _context.BookNotes.IgnoreQueryFilters().FirstOrDefaultAsync(n => n.Id == noteId);
            if (note != null)
            {
                note.IsDeleted = false;
                await _context.SaveChangesAsync();
            }
        }

        public async Task PermanentlyDeleteNoteAsync(Guid noteId)
        {
            _context.ChangeTracker.Clear(); // 【核心修复】
            var note = await _context.BookNotes.IgnoreQueryFilters().FirstOrDefaultAsync(n => n.Id == noteId);
            if (note != null)
            {
                _context.BookNotes.Remove(note);
                await _context.SaveChangesAsync();
            }
        }
    }
}