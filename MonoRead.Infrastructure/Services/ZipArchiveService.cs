using MonoRead.Core.Interfaces;
using MonoRead.Infrastructure.Logging;
using System.IO.Compression;

namespace MonoRead.Infrastructure.Services
{
    public class ZipArchiveService : IZipArchiveService
    {
        public async Task<bool> CreateBackupPackageAsync(string dbFilePath, string libraryFolderPath, string outputZipPath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (File.Exists(outputZipPath)) File.Delete(outputZipPath);

                    using var archive = ZipFile.Open(outputZipPath, ZipArchiveMode.Create);

                    // 1. 安全拷贝数据库文件 (包括 WAL 和 SHM 文件如果存在)
                    string dbDir = Path.GetDirectoryName(dbFilePath)!;
                    string dbName = Path.GetFileName(dbFilePath);
                    var dbFiles = Directory.GetFiles(dbDir, $"{dbName}*"); // 抓取 .db, .db-shm, .db-wal

                    foreach (var file in dbFiles)
                    {
                        // 锁保护：在 App 运行时，EF Core 可能锁住了文件，我们需要以 FileShare.ReadWrite 打开复制
                        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        var entry = archive.CreateEntry(Path.GetFileName(file), CompressionLevel.Fastest);
                        using var entryStream = entry.Open();
                        fs.CopyTo(entryStream);
                    }

                    // 2. 拷贝沙盒书库 Library 文件夹
                    if (Directory.Exists(libraryFolderPath))
                    {
                        var datFiles = Directory.GetFiles(libraryFolderPath, "*.dat");
                        foreach (var file in datFiles)
                        {
                            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                            var entry = archive.CreateEntry($"Library/{Path.GetFileName(file)}", CompressionLevel.Fastest);
                            using var entryStream = entry.Open();
                            fs.CopyTo(entryStream);
                        }
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    LocalLogger.LogError($"创建备份压缩包失败: {ex.Message}");
                    return false;
                }
            });
        }

        public async Task<bool> ExtractBackupPackageAsync(string zipFilePath, string targetAppDataPath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // 暴力覆盖解压
                    using var archive = ZipFile.OpenRead(zipFilePath);
                    foreach (var entry in archive.Entries)
                    {
                        string destinationPath = Path.GetFullPath(Path.Combine(targetAppDataPath, entry.FullName));
                        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                        entry.ExtractToFile(destinationPath, overwrite: true);
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    LocalLogger.LogError($"解压备份包失败: {ex.Message}");
                    return false;
                }
            });
        }
    }
}