using System;

namespace MonoRead.Core.Entities
{
    public class BookNote : BaseEntity
    {
        // 【核心修复】：必须带上问号 (?)，将其声明为 Nullable<Guid>，允许失去宿主
        public Guid? BookId { get; set; }
        public Guid? ChapterId { get; set; }

        // 历史快照，供 UI 展示
        public string BookTitle { get; set; } = string.Empty;

        public string SelectedText { get; set; } = string.Empty;
        public string UserComment { get; set; } = string.Empty;

        public bool IsOrphan { get; set; } = false;
        public bool IsDeleted { get; set; } = false;
    }
}