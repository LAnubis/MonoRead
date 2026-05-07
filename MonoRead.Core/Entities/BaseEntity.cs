using System;
using System.Collections.Generic;
using System.Text;

namespace MonoRead.Core.Entities
{
    public abstract class BaseEntity
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        // 软删除标记
        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }

        // 审计时间戳
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow.ToLocalTime();
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow.ToLocalTime();
    }
}
