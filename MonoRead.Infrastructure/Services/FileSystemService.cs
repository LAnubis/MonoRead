using Microsoft.Maui.Storage;
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

        // 【核心修改】直接读取安全的系统流并写入沙盒
        public async Task<string> CopyFileToSandboxAsync(Stream sourceStream, string targetFileName)
        {
            string targetPath = Path.Combine(GetSandboxDirectory(), targetFileName);

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
