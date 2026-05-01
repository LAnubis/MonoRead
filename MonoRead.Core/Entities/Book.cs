using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace MonoRead.Core.Entities
{
    // 继承 BaseEntity，并实现 INotifyPropertyChanged 驱动 UI 刷新
    public class Book : BaseEntity, INotifyPropertyChanged
    {
        public Guid? FolderId { get; set; }
        public string Title { get; set; } = string.Empty;
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