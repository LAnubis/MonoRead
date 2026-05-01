using Microsoft.EntityFrameworkCore;
using MonoRead.Core.Entities;
using MonoRead.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;

namespace MonoRead.Infrastructure.Services
{
    public class FolderRepository : IFolderRepository
    {
        private readonly AppDbContext _dbContext;

        public FolderRepository(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<IEnumerable<Folder>> GetAllAsync()
        {
            return await _dbContext.Folders.AsNoTracking().ToListAsync();
        }

        public async Task<Folder?> GetByIdAsync(Guid id)
        {
            return await _dbContext.Folders.FirstOrDefaultAsync(f => f.Id == id);
        }

        public async Task AddAsync(Folder folder)
        {
            await _dbContext.Folders.AddAsync(folder);
            await _dbContext.SaveChangesAsync();
        }

        public async Task UpdateAsync(Folder folder)
        {
            _dbContext.Folders.Update(folder);
            await _dbContext.SaveChangesAsync();
        }

        public async Task DeleteAsync(Folder folder)
        {
            _dbContext.Folders.Remove(folder);
            await _dbContext.SaveChangesAsync();
        }
    }
}
