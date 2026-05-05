using System;
using System.Collections.Generic;
using System.Text;

namespace MonoRead.Core.Entities
{
    public class WebDavFileNode
    {
        public string Href { get; set; } = string.Empty;       // 文件完整相对路径
        public string DisplayName { get; set; } = string.Empty; // 显示名称
        public bool IsDirectory { get; set; }                  // 是否为文件夹
        public long ContentLength { get; set; }                // 文件大小
        public DateTime LastModified { get; set; }             // 最后修改时间
    }
}
