using System;
using System.Collections.Generic;
using System.Text;

namespace MonoRead.Core.Entities
{
    public class Folder : BaseEntity
    {
        public string Name { get; set; } = string.Empty;
        public int SortOrder { get; set; }

        // 导航属性
        public virtual ICollection<Book> Books { get; set; } = new List<Book>();
    }
}
