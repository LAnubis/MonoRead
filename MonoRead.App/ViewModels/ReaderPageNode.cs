using System;
using System.Collections.Generic;
using System.Text;

namespace MonoRead.App.ViewModels
{
    public class ReaderPageNode
    {
        // 本页所属的章节 ID
        public Guid ChapterId { get; set; }

        // 本页所属的章节标题（用于顶部/底部状态栏显示）
        public string ChapterTitle { get; set; } = string.Empty;

        // 这一页被切出来的真实正文内容
        public string Content { get; set; } = string.Empty;

        // 这一页是该章的第几页
        public int PageIndex { get; set; }

        // 该章总共有多少页
        public int TotalPagesInChapter { get; set; }
    }
}
