using System;
using System.Collections.Generic;
using System.Text;

namespace MonoRead.Core.Interfaces
{
    public interface IFileSystemService
    {
        // 获取应用私有沙盒根目录
        string GetSandboxDirectory();

        // 计算文件的 SHA256 防重哈希
        Task<string> CalculateFileHashAsync(string sourceFilePath);

        // 将外部文件拷贝至沙盒，并重命名为 {Guid}.dat
        Task<string> CopyFileToSandboxAsync(string sourceFilePath, Guid bookId);

        // 物理彻底删除沙盒中的文件
        void DeletePhysicalFile(string filePath);
    }
}
