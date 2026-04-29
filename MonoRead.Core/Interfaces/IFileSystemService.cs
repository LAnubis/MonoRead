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

        // 【核心修改】不要传路径了，直接传文件流和目标文件名
        Task<string> CopyFileToSandboxAsync(Stream sourceStream, string targetFileName);

        // 物理彻底删除沙盒中的文件
        void DeletePhysicalFile(string filePath);
    }
}
