using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Runtime.CompilerServices;

namespace MonoRead.Core.Entities
{
    public class Book : BaseEntity, INotifyPropertyChanged
    {
        public Guid? FolderId { get; set; }
        public string Title { get; set; } = string.Empty;

        // 虽然砍掉了在线搜索，但导入本地 TXT 时依然可能需要这些元数据
        public string Author { get; set; } = "未知";
        public string Description { get; set; } = string.Empty;
        public string CoverUrl { get; set; } = string.Empty;

        public string FilePath { get; set; } = string.Empty;
        public string? CoverImagePath { get; set; }
        public string FileHash { get; set; } = string.Empty;
        public DateTime ImportDate { get; set; }

        // =========================================================
        // 【新增】：专用于精准断点续传的强类型进度锚点
        // (彻底删除了混乱的 ProgressLocator JSON 字符串)
        // =========================================================
        public Guid? LastReadChapterId { get; set; }
        public int LastReadOffset { get; set; }

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

                // 【核心重构】：直接使用强类型的 LastReadChapterId，告别繁琐且易错的 JSON 解析
                if (LastReadChapterId.HasValue)
                {
                    var chapter = Chapters.FirstOrDefault(c => c.Id == LastReadChapterId.Value);
                    if (chapter != null)
                    {
                        // 假设 SortOrder 是从 0 开始的索引，展示给用户时 +1
                        current = chapter.SortOrder + 1;
                    }
                }

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