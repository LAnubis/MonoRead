using CommunityToolkit.Mvvm.Messaging.Messages;
using MonoRead.Core.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace MonoRead.Core.Messages
{
    // 当新书解析并写入沙盒成功后，携带完整的书籍实体通知书架刷新
    public class BookImportedMessage : ValueChangedMessage<Book>
    {
        public BookImportedMessage(Book newBook) : base(newBook)
        {
        }
    }
}
