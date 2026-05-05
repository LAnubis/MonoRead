using MonoRead.Core.Interfaces;

namespace MonoRead.UseCase
{
    public interface ICloudBackupUseCase
    {
        // 【架构纠偏】：路径由外部（UI层）传入，保持 UseCase 层的绝对纯洁
        Task<string> ExecuteBackupAsync(string url, string user, string pass, string appDataPath, string cachePath);
        Task<string> ExecuteRestoreAsync(string url, string user, string pass, string appDataPath, string cachePath);
    }

    public class CloudBackupUseCase : ICloudBackupUseCase
    {
        private readonly ICloudStorageService _cloudStorageService;
        private readonly IZipArchiveService _zipArchiveService;
        private const string BACKUP_FOLDER = "MonoRead_Backup";

        public CloudBackupUseCase(ICloudStorageService cloudStorageService, IZipArchiveService zipArchiveService)
        {
            _cloudStorageService = cloudStorageService;
            _zipArchiveService = zipArchiveService;
        }

        public async Task<string> ExecuteBackupAsync(string url, string user, string pass, string appDataPath, string cachePath)
        {
            // 1. 确保云端专属目录存在
            await _cloudStorageService.EnsureDirectoryExistsAsync(url, user, pass, BACKUP_FOLDER);

            // 2. 本地打包 (使用外部传入的纯净路径)
            string tempZipPath = Path.Combine(cachePath, $"AutoBak_{DateTime.Now:yyyyMMdd_HHmmss}.monobak");
            string dbPath = Path.Combine(appDataPath, "monoread.db");
            string libraryPath = Path.Combine(appDataPath, "Library");

            bool zipSuccess = await _zipArchiveService.CreateBackupPackageAsync(dbPath, libraryPath, tempZipPath);
            if (!zipSuccess) throw new Exception("本地数据打包失败，请检查手机存储空间。");

            // 3. 上传到云端
            string remotePath = $"{BACKUP_FOLDER}/{Path.GetFileName(tempZipPath)}";
            bool uploadSuccess = await _cloudStorageService.UploadFileAsync(url, user, pass, tempZipPath, remotePath);
            if (!uploadSuccess) throw new Exception("上传到坚果云失败，请检查网络。");

            // 4. 清理本地临时文件
            if (File.Exists(tempZipPath)) File.Delete(tempZipPath);

            // 5. 【核心轮转】：检查云端文件，只保留最新的 3 份
            var allFiles = await _cloudStorageService.ListFilesAsync(url, user, pass, BACKUP_FOLDER);
            var backupFiles = allFiles.Where(f => f.DisplayName.EndsWith(".monobak"))
                                      .OrderByDescending(f => f.DisplayName) // 按时间戳倒序
                                      .ToList();

            if (backupFiles.Count > 3)
            {
                // 删除第 4 个及以后的陈旧备份
                var filesToDelete = backupFiles.Skip(3).ToList();
                foreach (var file in filesToDelete)
                {
                    await _cloudStorageService.DeleteFileAsync(url, user, pass, file.Href);
                }
            }

            return "备份成功！云端已安全留存您的数据快照。";
        }

        public async Task<string> ExecuteRestoreAsync(string url, string user, string pass, string appDataPath, string cachePath)
        {
            // 1. 扫描云端目录寻找最新的备份
            var allFiles = await _cloudStorageService.ListFilesAsync(url, user, pass, BACKUP_FOLDER);
            var latestBackup = allFiles.Where(f => f.DisplayName.EndsWith(".monobak"))
                                       .OrderByDescending(f => f.DisplayName)
                                       .FirstOrDefault();

            if (latestBackup == null) throw new Exception("云端未找到任何可用的 MonoRead 备份文件。");

            // 2. 下载最新的备份 (使用外部传入的纯净路径)
            string tempZipPath = Path.Combine(cachePath, "RestoreTemp.monobak");
            if (File.Exists(tempZipPath)) File.Delete(tempZipPath);

            bool downSuccess = await _cloudStorageService.DownloadFileAsync(url, user, pass, latestBackup.Href, tempZipPath);
            if (!downSuccess) throw new Exception("从云端下载备份文件失败。");

            // 3. 暴力解压覆盖本地 AppData (危险操作)
            bool extractSuccess = await _zipArchiveService.ExtractBackupPackageAsync(tempZipPath, appDataPath);
            if (File.Exists(tempZipPath)) File.Delete(tempZipPath);

            if (!extractSuccess) throw new Exception("解压覆盖失败。");

            return "恢复成功";
        }
    }
}