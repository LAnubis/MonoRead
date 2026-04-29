using System;
using System.Collections.Generic;
using System.Text;

namespace MonoRead.Core.Interfaces
{
    public interface IArchiveBookUseCase
    {
        // 软删除：仅修改 IsDeleted 标记，移入回收站
        Task MoveBookToRecycleBinAsync(Guid bookId);

        // 恢复书籍：从回收站移出，如果原孤儿笔记存在则解除其孤儿状态
        Task RestoreBookAsync(Guid bookId);

        // 彻底物理销毁：跨资源事务（删除沙盒物理文件 + 标记关联笔记为 IsOrphan）
        Task DestroyBookPhysicallyAsync(Guid bookId);
    }
}
