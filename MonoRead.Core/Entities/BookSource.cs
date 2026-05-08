using System;
using System.Collections.Generic;
using System.Text;

namespace MonoRead.Core.Entities
{
    public class BookSource : BaseEntity
    {
        public string Name { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;

        // 核心：存储整个 JSON 规则字符串
        public string RulesJson { get; set; } = string.Empty;

        // 也可以预留一些常用字段方便列表展示
        public string Author { get; set; } = "未知";
    }
}
