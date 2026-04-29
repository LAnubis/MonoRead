using MonoRead.Core.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace MonoRead.Core.Interfaces
{
    public interface IBookParsingUseCase
    {
        // 传入文件沙盒路径、书籍名称、哈希值，返回解析完毕并存入数据库的书籍实体
        Task<Book> ParseAndSplitBookAsync(string sandboxFilePath, string fileName, string fileHash);
    }
}
