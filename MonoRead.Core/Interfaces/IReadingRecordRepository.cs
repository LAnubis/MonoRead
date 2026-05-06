using MonoRead.Core.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace MonoRead.Core.Interfaces
{
    public interface IReadingRecordRepository
    {
        // 增加阅读时长（如果今天没记录就创建，有记录就累加）
        Task AddDurationAsync(DateTime date, int seconds);
        // 获取过去 N 天的阅读数据（用于画日历热力图）
        Task<List<ReadingRecord>> GetRecentRecordsAsync(int days);
    }
}
