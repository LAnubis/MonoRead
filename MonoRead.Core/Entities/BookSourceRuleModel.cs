using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace MonoRead.Core.Entities
{
    // 这个类专门用来接收 JSON 反序列化，不直接存入数据库，而是作为 BookSource 的 RulesJson 的解析产物
    public class BookSourceRuleModel
    {
        [JsonPropertyName("sourceName")]
        public string SourceName { get; set; } = string.Empty;

        [JsonPropertyName("baseUrl")]
        public string BaseUrl { get; set; } = string.Empty;

        [JsonPropertyName("searchUrl")]
        public string SearchUrl { get; set; } = string.Empty;

        [JsonPropertyName("charset")]
        public string Charset { get; set; } = "utf-8";

        [JsonPropertyName("ruleSearch")]
        public RuleSearch RuleSearch { get; set; } = new();

        [JsonPropertyName("ruleDetail")]
        public RuleDetail RuleDetail { get; set; } = new();

        // ==========================================
        // 【V2 新增】：目录解析规则与正文解析规则
        // ==========================================
        [JsonPropertyName("ruleToc")]
        public RuleTocModel RuleToc { get; set; } = new();

        [JsonPropertyName("ruleContent")]
        public RuleContentModel RuleContent { get; set; } = new();
    }

    public class RuleSearch
    {
        [JsonPropertyName("bookList")]
        public string BookList { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("author")]
        public string Author { get; set; } = string.Empty;

        [JsonPropertyName("coverUrl")]
        public string CoverUrl { get; set; } = string.Empty;

        [JsonPropertyName("detailUrl")]
        public string DetailUrl { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;
    }

    public class RuleDetail
    {
        [JsonPropertyName("txtDownloadUrl")]
        public string TxtDownloadUrl { get; set; } = string.Empty;
    }

    // ==========================================
    // 【V2 新增模型】：目录规则定义
    // ==========================================
    public class RuleTocModel
    {
        [JsonPropertyName("chapterList")]
        public string ChapterList { get; set; } = string.Empty;

        [JsonPropertyName("chapterName")]
        public string ChapterName { get; set; } = string.Empty;

        [JsonPropertyName("chapterUrl")]
        public string ChapterUrl { get; set; } = string.Empty;
    }

    // ==========================================
    // 【V2 新增模型】：正文规则定义
    // ==========================================
    public class RuleContentModel
    {
        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }
}