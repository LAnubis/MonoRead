using MonoRead.Core.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace MonoRead.Core.Interfaces
{
    public interface IBookSourceRepository
    {
        Task<List<BookSource>> GetAllAsync();
        Task AddAsync(BookSource source);
        Task UpdateAsync(BookSource source);
        Task DeleteAsync(BookSource source);
    }
}
