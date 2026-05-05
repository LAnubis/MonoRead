using MonoRead.Core.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace MonoRead.Core.Interfaces
{
    public interface ICloudStorageService
    {
        // 测试账号密码是否能连接成功
        Task<bool> TestConnectionAsync(string serverUrl, string username, string password);

        // 获取指定目录下的文件列表（主要用来找 .txt 文件）
        Task<List<WebDavFileNode>> ListFilesAsync(string serverUrl, string username, string password, string directoryPath);

        // 将网盘文件下载到本地物理路径（如沙盒目录）
        Task<bool> DownloadFileAsync(string serverUrl, string username, string password, string remoteFilePath, string localSavePath, IProgress<double>? progress = null);
    }
}
