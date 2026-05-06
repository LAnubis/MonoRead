using MonoRead.Core.Entities;
using MonoRead.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MonoRead.Infrastructure.Services
{
    public class ReadingRecordRepository : IReadingRecordRepository
    {
        private readonly AppDbContext _context;
        public ReadingRecordRepository(AppDbContext context) => _context = context;

        public async Task AddDurationAsync(DateTime date, int seconds)
        {
            if (seconds <= 0) return;

            var targetDate = date.Date; // 抹除时分秒，只保留日期

            // ==============================================================================
            // 【核心修复】：将条件拆分到 Where 中，彻底消除 CS0411 编译器的泛型推断歧义
            // ==============================================================================
            var record = await _context.ReadingRecords
                .Where(r => r.RecordDate == targetDate)
                .FirstOrDefaultAsync();

            if (record == null)
            {
                record = new ReadingRecord { Id = Guid.NewGuid(), RecordDate = targetDate, DurationSeconds = seconds };
                await _context.ReadingRecords.AddAsync(record);
            }
            else
            {
                record.DurationSeconds += seconds;
                _context.ReadingRecords.Update(record);
            }

            await _context.SaveChangesAsync();
        }

        public async Task<List<ReadingRecord>> GetRecentRecordsAsync(int days)
        {
            var startDate = DateTime.UtcNow.Date.AddDays(-days);
            return await _context.ReadingRecords
                .Where(r => r.RecordDate >= startDate)
                .OrderBy(r => r.RecordDate)
                .ToListAsync();
        }
    }
}
