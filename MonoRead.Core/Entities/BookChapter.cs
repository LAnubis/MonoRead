using System;
using System.Collections.Generic;
using System.Text;

namespace MonoRead.Core.Entities
{
    public class BookChapter : BaseEntity
    {
        public Guid BookId { get; set; }
        public string Title { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public string StartLocator { get; set; } = "{}"; // 章节起始位置 JSON

        // 导航属性
        public virtual Book Book { get; set; } = null!;
    }
}
