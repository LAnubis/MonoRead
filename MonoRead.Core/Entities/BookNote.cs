using System;
using System.Collections.Generic;
using System.Text;

namespace MonoRead.Core.Entities
{
    public class BookNote : BaseEntity
    {
        public Guid BookId { get; set; }
        // 【核心修复】：补齐缺失的 ChapterId 定义，消除 CS1061 错误
        public Guid ChapterId { get; set; }
        public string BookTitle { get; set; } = string.Empty;
        public string SelectedText { get; set; } = string.Empty; // 划线摘录的句子
        public string UserComment { get; set; } = string.Empty;  // 用户的个人笔记
        public bool IsDeleted { get; set; } = false;             // 预留给回收站
    }
}
