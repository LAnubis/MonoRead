using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using System.Text.Json;

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
                                                            // 补全这两个缺失的属性
        public DateTime ImportDate { get; set; }
        // 导航属性
        public virtual Folder? Folder { get; set; }
        public virtual ICollection<BookChapter> Chapters { get; set; } = new List<BookChapter>();
        public virtual ICollection<HighlightNote> Notes { get; set; } = new List<HighlightNote>();

        // 【核心新增】UI 绑定的进度字符串。由于加了 [NotMapped]，EF Core 会忽略它，不需要写迁移！
        [NotMapped]
        public string ProgressText
        {
            get
            {
                if (Chapters == null || !Chapters.Any()) return "未解析";

                int total = Chapters.Count;
                int current = 1; // 默认第一章

                if (!string.IsNullOrWhiteSpace(ProgressLocator) && ProgressLocator != "{}")
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(ProgressLocator);
                        if (doc.RootElement.TryGetProperty("chapterId", out var chapterIdElement))
                        {
                            var chapterId = chapterIdElement.GetGuid();
                            var chapter = Chapters.FirstOrDefault(c => c.Id == chapterId);
                            if (chapter != null)
                            {
                                // 假设 SortOrder 是从 0 开始的，加 1 就是日常说的第几章
                                current = chapter.SortOrder + 1;
                            }
                        }
                    }
                    catch { /* 解析失败则回退到第一章 */ }
                }
                return $"{current}章 / 共{total}章";
            }
        }
    }
}
