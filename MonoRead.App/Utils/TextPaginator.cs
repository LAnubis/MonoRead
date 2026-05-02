using MonoRead.App.ViewModels;
using System;
using System.Collections.Generic;
using System.Text;

namespace MonoRead.App.Utils
{
    public static class TextPaginator
    {
        /// <summary>
        /// 将超长字符串根据当前字号和屏幕估算尺寸，切分为多个页面节点
        /// </summary>
        public static List<ReaderPageNode> Paginate(string fullText, Guid chapterId, string chapterTitle, int fontSize)
        {
            var pages = new List<ReaderPageNode>();
            if (string.IsNullOrWhiteSpace(fullText)) return pages;

            // 【核心估算参数】（您可以根据真机测试结果微调这些常数）
            // 假设设备屏幕可用宽度约 360dp，高度约 700dp，边距 40dp
            double usableWidth = 320;
            double usableHeight = 650;

            // 粗略计算一行能放多少字，一屏能放多少行 (考虑 1.8 的行高)
            int charsPerLine = (int)(usableWidth / (fontSize * 1.0));
            int linesPerPage = (int)(usableHeight / (fontSize * 1.8));

            // 一页能容纳的最多字符数
            int charsPerPage = charsPerLine * linesPerPage;

            // 为了防止溢出导致文字被截断，我们保守计算，预留 15% 的安全边际
            charsPerPage = (int)(charsPerPage * 0.85);
            if (charsPerPage < 50) charsPerPage = 50; // 兜底

            int totalLength = fullText.Length;
            int currentIndex = 0;
            int pageIndex = 1;

            // 开始切片
            while (currentIndex < totalLength)
            {
                int lengthToCut = Math.Min(charsPerPage, totalLength - currentIndex);

                // 优化：尽量不要在一段话中间切断，寻找最近的段落换行符 '\n'（高级特性，MVP先按定长切）
                // 现阶段采取定长硬切
                string pageContent = fullText.Substring(currentIndex, lengthToCut);

                pages.Add(new ReaderPageNode
                {
                    ChapterId = chapterId,
                    ChapterTitle = chapterTitle,
                    Content = pageContent,
                    PageIndex = pageIndex
                });

                currentIndex += lengthToCut;
                pageIndex++;
            }

            // 回填该章总页数
            foreach (var p in pages) p.TotalPagesInChapter = pages.Count;

            return pages;
        }
    }
}
