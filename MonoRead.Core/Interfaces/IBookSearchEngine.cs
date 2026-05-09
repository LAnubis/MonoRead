using MonoRead.Core.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace MonoRead.Core.Interfaces
{
    // 定义解析引擎必须具备的能力
    public interface IBookSearchEngine
    {
        // 传入书源规则和关键词，返回解析好的书籍列表
        Task<List<Book>> SearchBooksAsync(BookSourceRuleModel rule, string keyword);

        // 传入详情页规则和书籍链接，返回真实的 TXT 下载地址
        Task<string> GetDownloadUrlAsync(RuleDetail rule, string detailUrl);


        // 【新增】：获取全本目录
        Task<List<ChapterNode>> GetTocAsync(BookSourceRuleModel rule, string tocUrl);

        // 【新增】：获取单章正文
        Task<string> GetChapterContentAsync(BookSourceRuleModel rule, string chapterUrl);
    }
}
