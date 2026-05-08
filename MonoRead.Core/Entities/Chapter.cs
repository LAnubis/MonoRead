using System;
using System.Collections.Generic;
using System.Text;

namespace MonoRead.Core.Entities
{
    /// <summary>
    /// 章节实体类 (V2 架构核心模型)
    /// </summary>
    public class Chapter
    {
        /// <summary>
        /// 章节唯一主键 ID
        /// </summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// 所属的书籍 ID (关联外键，用于以后存入 SQLite)
        /// </summary>
        public Guid BookId { get; set; }

        /// <summary>
        /// 章节序号 (极其重要！因为多线程并发下载时，章节顺序会乱，必须靠它排序)
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// 章节标题 (例如: "第一章 虫子")
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// 章节的真实网络请求 URL
        /// </summary>
        public string Url { get; set; } = string.Empty;

        /// <summary>
        /// 章节正文内容 (抓取后缓存在这里，下次打开直接读本地)
        /// </summary>
        public string Content { get; set; } = string.Empty;
    }
}
