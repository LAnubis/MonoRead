using CommunityToolkit.Mvvm.Messaging.Messages;
using System;
using System.Collections.Generic;
using System.Text;

namespace MonoRead.Core.Messages
{
    // 当书籍被软删除或彻底销毁时，携带对应书籍的 Id 广播给全 App
    public class BookDeletedMessage : ValueChangedMessage<Guid>
    {
        public BookDeletedMessage(Guid bookId) : base(bookId)
        {
        }
    }
}
