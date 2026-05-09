using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace MonoRead.Core.Entities
{
    public class Book : BaseEntity, INotifyPropertyChanged
    {
        public Guid? FolderId { get; set; }
        public string Title { get; set; } = string.Empty;


        // =========================================================
        // 【新增】：为了承接网络书源而扩展的元数据字段
        // =========================================================
        public string Author { get; set; } = "未知";
        public string Description { get; set; } = string.Empty;
        public string CoverUrl { get; set; } = string.Empty; // 存储网络图片的直链


        public string FilePath { get; set; } = string.Empty;
        public string? CoverImagePath { get; set; }
        public string FileHash { get; set; } = string.Empty;
        public string ProgressLocator { get; set; } = "{}";
        public DateTime ImportDate { get; set; }

        public virtual Folder? Folder { get; set; }
        public virtual ICollection<BookChapter> Chapters { get; set; } = new List<BookChapter>();
        public virtual ICollection<HighlightNote> Notes { get; set; } = new List<HighlightNote>();

        // ================= UI 状态 (不映射到数据库) =================
        private bool _isSelected;

        [NotMapped]
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // ================= 动态进度文本 =================
        [NotMapped]
        public string ProgressText
        {
            get
            {
                // 【防御】：如果没有加载出章节，说明 EF Core 没有 Include
                if (Chapters == null || !Chapters.Any()) return "未解析";

                int total = Chapters.Count;
                int current = 1;

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
                                // 假设 SortOrder 是从 0 开始的索引，展示给用户时 +1
                                current = chapter.SortOrder + 1;
                            }
                        }
                    }
                    catch { /* JSON 破损等解析失败情况，静默回退到第一章 */ }
                }

                // 【核心修复 2】：规范化输出格式
                return $"{current}章 / 共{total}章";
            }
        }
    }

    public class ChapterNode
    {
        public string Title { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
    }
}