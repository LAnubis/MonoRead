using MonoRead.Core.Entities;

namespace MonoRead.Core.Interfaces
{
    public interface ICloudStorageService
    {
        Task<bool> TestConnectionAsync(string serverUrl, string username, string password);
        Task<List<WebDavFileNode>> ListFilesAsync(string serverUrl, string username, string password, string directoryPath);
        Task<bool> DownloadFileAsync(string serverUrl, string username, string password, string remoteFilePath, string localSavePath, IProgress<double>? progress = null);

        // 【新增】备份所需的三大核心能力
        Task<bool> EnsureDirectoryExistsAsync(string serverUrl, string username, string password, string directoryPath);
        Task<bool> UploadFileAsync(string serverUrl, string username, string password, string localFilePath, string remoteFilePath, IProgress<double>? progress = null);
        Task<bool> DeleteFileAsync(string serverUrl, string username, string password, string remoteFilePath);
    }
}