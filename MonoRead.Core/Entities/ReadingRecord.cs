using System;
using System.Collections.Generic;
using System.Text;

namespace MonoRead.Core.Entities
{
    // 每日阅读统计记录
    public class ReadingRecord : BaseEntity
    {
        // 记录日期 (仅保留年月日，如 2026-05-06)
        public DateTime RecordDate { get; set; }

        // 当天总阅读时长（秒）
        public int DurationSeconds { get; set; } = 0;
    }
}
