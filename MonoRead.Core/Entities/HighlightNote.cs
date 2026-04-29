using System;
using System.Collections.Generic;
using System.Text;

namespace MonoRead.Core.Entities
{
    public class HighlightNote : BaseEntity
    {
        public Guid BookId { get; set; }
        public string OriginalText { get; set; } = string.Empty; // 划线原文
        public string? UserComment { get; set; } // 用户附加想法
        public string StartLocator { get; set; } = "{}"; // 划线起点 JSON
        public string EndLocator { get; set; } = "{}"; // 划线终点 JSON
        public string Color { get; set; } = "#B0BEC5"; // [V1 MVP 必做] 莫兰迪色值

        // 状态扭转逻辑
        public bool IsOrphan { get; set; } // 孤儿标记（原书已彻底物理删除）

        // 导航属性
        public virtual Book Book { get; set; } = null!;
    }
}
