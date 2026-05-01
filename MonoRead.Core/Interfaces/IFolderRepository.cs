using MonoRead.Core.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace MonoRead.Core.Interfaces
{
    public interface IFolderRepository
    {
        Task<IEnumerable<Folder>> GetAllAsync();
        Task<Folder?> GetByIdAsync(Guid id);
        Task AddAsync(Folder folder);
        Task UpdateAsync(Folder folder);
        Task DeleteAsync(Folder folder);
    }
}
