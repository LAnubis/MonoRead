using System;
using System.Collections.Generic;
using System.Text;

namespace MonoRead.Core.Entities
{
    // MonoRead.Core/Entities/Book.cs
    public class Book : BaseEntity
    {
        public Guid? FolderId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty; // App 私有沙盒路径
        public string? CoverImagePath { get; set; } // [V1 MVP 必做]
        public string FileHash { get; set; } = string.Empty; // SHA256 防重索引
        public string ProgressLocator { get; set; } = "{}"; // JSON 格式定位协议

        // 导航属性
        public virtual Folder? Folder { get; set; }
        public virtual ICollection<BookChapter> Chapters { get; set; } = new List<BookChapter>();
        public virtual ICollection<HighlightNote> Notes { get; set; } = new List<HighlightNote>();
    }
}
