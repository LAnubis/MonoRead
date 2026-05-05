using System;
using System.Collections.Generic;
using System.Text;

namespace MonoRead.Core.Interfaces
{
    public interface IZipArchiveService
    {
        Task<bool> CreateBackupPackageAsync(string dbFilePath, string libraryFolderPath, string outputZipPath);
        Task<bool> ExtractBackupPackageAsync(string zipFilePath, string targetAppDataPath);
    }
}
