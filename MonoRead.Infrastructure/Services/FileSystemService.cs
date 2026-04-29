using MonoRead.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace MonoRead.Infrastructure.Services
{
    public class FileSystemService : IFileSystemService
    {
        public string GetSandboxDirectory()
        {
            return FileSystem.AppDataDirectory;
        }

        public async Task<string> CalculateFileHashAsync(string sourceFilePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(sourceFilePath);
            var hashBytes = await sha256.ComputeHashAsync(stream);
            // 转为小写的十六进制字符串
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        public async Task<string> CopyFileToSandboxAsync(string sourceFilePath, Guid bookId)
        {
            // 我们统一将导入的文件重命名为 "BookId.txt" 存入沙盒
            string ext = Path.GetExtension(sourceFilePath);
            string newFileName = $"{bookId}{ext}";
            string targetPath = Path.Combine(GetSandboxDirectory(), newFileName);

            using var sourceStream = File.OpenRead(sourceFilePath);
            using var targetStream = File.Create(targetPath);
            await sourceStream.CopyToAsync(targetStream);

            return targetPath;
        }

        public void DeletePhysicalFile(string filePath)
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }
}
