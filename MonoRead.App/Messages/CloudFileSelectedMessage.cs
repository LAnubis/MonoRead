namespace MonoRead.App.Messages
{
    // 用于云盘选择器选定文件后通知书架页
    public record CloudFileSelectedMessage(string RemoteFilePath, string DisplayName);
}