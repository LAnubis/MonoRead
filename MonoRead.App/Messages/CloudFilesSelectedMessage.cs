using System.Collections.Generic;

namespace MonoRead.App.Messages
{
    // 用于云盘选择器选定多个文件后通知书架页
    // 使用 List 传递多个包含远程路径和文件名的元组
    public record CloudFilesSelectedMessage(List<(string RemoteFilePath, string DisplayName)> SelectedFiles);
}