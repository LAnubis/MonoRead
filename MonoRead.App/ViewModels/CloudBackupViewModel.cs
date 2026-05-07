using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonoRead.Core.Interfaces;
using MonoRead.UseCase;

namespace MonoRead.App.ViewModels
{
    public partial class CloudBackupViewModel : ObservableObject
    {
        private readonly ICloudStorageService _cloudStorageService;
        private readonly ICloudBackupUseCase _backupUseCase;

        [ObservableProperty] private string _serverUrl = "https://dav.jianguoyun.com/dav/";
        [ObservableProperty] private string _username = string.Empty;
        [ObservableProperty] private string _password = string.Empty;

        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private string _testResultText = "暂未测试";
        [ObservableProperty] private Color _testResultColor = Colors.Gray;

        // 渐进式展示控制
        [ObservableProperty] private bool _isConfigValid = false;
        [ObservableProperty] private string _lastBackupTimeText = "未获取";

        public CloudBackupViewModel(ICloudStorageService cloudStorageService, ICloudBackupUseCase backupUseCase)
        {
            _cloudStorageService = cloudStorageService;
            _backupUseCase = backupUseCase;
            LoadConfigAsync();
        }

        private async void LoadConfigAsync()
        {
            var savedUrl = await SecureStorage.Default.GetAsync("WebDav_Url");
            var savedUser = await SecureStorage.Default.GetAsync("WebDav_User");
            var savedPass = await SecureStorage.Default.GetAsync("WebDav_Pass");

            if (!string.IsNullOrEmpty(savedUrl)) ServerUrl = savedUrl;
            if (!string.IsNullOrEmpty(savedUser)) Username = savedUser;
            if (!string.IsNullOrEmpty(savedPass)) Password = savedPass;

            // 如果有密码，自动跑一次静默测试以展开下方的控制台
            if (!string.IsNullOrEmpty(Password))
            {
                await SaveAndTestAsync();
            }
        }

        [RelayCommand]
        private async Task SaveAndTestAsync()
        {
            if (string.IsNullOrWhiteSpace(ServerUrl) || string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
            {
                TestResultText = "请填写完整配置！"; TestResultColor = Colors.Red; IsConfigValid = false; return;
            }

            if (IsBusy) return;
            IsBusy = true;
            TestResultText = "正在验证坚果云身份..."; TestResultColor = Colors.Orange;

            try
            {
                bool isSuccess = await _cloudStorageService.TestConnectionAsync(ServerUrl, Username, Password);
                if (isSuccess)
                {
                    TestResultText = "身份验证成功，云端数据中心已激活。"; TestResultColor = Colors.Green;
                    await SecureStorage.Default.SetAsync("WebDav_Url", ServerUrl);
                    await SecureStorage.Default.SetAsync("WebDav_User", Username);
                    await SecureStorage.Default.SetAsync("WebDav_Pass", Password);

                    IsConfigValid = true;
                    // 异步探测一下最近的备份时间
                    _ = CheckLastBackupTimeAsync();
                }
                else
                {
                    TestResultText = "❌ 验证失败，请检查账号和应用授权密码。"; TestResultColor = Colors.Red; IsConfigValid = false;
                }
            }
            catch (Exception) { TestResultText = "网络异常或请求超时"; TestResultColor = Colors.Red; IsConfigValid = false; }
            finally { IsBusy = false; }
        }

        private async Task CheckLastBackupTimeAsync()
        {
            try
            {
                var allFiles = await _cloudStorageService.ListFilesAsync(ServerUrl, Username, Password, "MonoRead_Backup");
                var latest = allFiles.Where(f => f.DisplayName.EndsWith(".monobak")).OrderByDescending(f => f.DisplayName).FirstOrDefault();
                if (latest != null)
                {
                    // 提取文件名中的时间 AutoBak_20260505_1813.monobak
                    string name = latest.DisplayName;
                    LastBackupTimeText = $"最近一次云端快照：{name.Substring(8, 4)}-{name.Substring(12, 2)}-{name.Substring(14, 2)} {name.Substring(17, 2)}:{name.Substring(19, 2)}";
                }
                else { LastBackupTimeText = "云端暂无数据备份"; }
            }
            catch { LastBackupTimeText = "云端暂无数据备份"; }
        }

        [RelayCommand]
        private async Task BackupDataAsync()
        {
            if (IsBusy) return;
            bool confirm = await Shell.Current.DisplayAlertAsync("创建云端快照", "这将会把您当前的书架、进度和笔记安全打包上传至坚果云。\n\n(将自动保留最近的 3 份快照)", "立即备份", "取消");
            if (!confirm) return;

            IsBusy = true;
            TestResultText = "正在打包并极速上传至坚果云..."; TestResultColor = Colors.Orange;

            try
            {
                //string msg = await _backupUseCase.ExecuteBackupAsync(ServerUrl, Username, Password);
                // 传入 MAUI 环境下的 FileSystem 路径
                string msg = await _backupUseCase.ExecuteBackupAsync(ServerUrl, Username, Password, FileSystem.AppDataDirectory, FileSystem.CacheDirectory);
                TestResultText = msg; TestResultColor = Colors.Green;
                await CheckLastBackupTimeAsync();
                await Shell.Current.DisplayAlertAsync("太棒了", "数据已安全触达云端。", "确定");
            }
            catch (Exception ex)
            {
                TestResultText = "备份失败"; TestResultColor = Colors.Red;
                await Shell.Current.DisplayAlertAsync("报错", ex.Message, "确定");
            }
            finally { IsBusy = false; }
        }

        [RelayCommand]
        private async Task RestoreDataAsync()
        {
            if (IsBusy) return;
            // 终极死亡警告！
            bool confirm1 = await Shell.Current.DisplayAlertAsync("⚠️ 危险操作警告", "从云端恢复将【彻底覆盖】您当前手机上的所有小说、进度和笔记！\n此操作不可逆！", "我已清楚风险，继续", "取消");
            if (!confirm1) return;

            IsBusy = true;
            TestResultText = "正在从坚果云拉取快照并覆盖沙盒..."; TestResultColor = Colors.Orange;

            try
            {
                // await _backupUseCase.ExecuteRestoreAsync(ServerUrl, Username, Password);
                // 传入 MAUI 环境下的 FileSystem 路径
                await _backupUseCase.ExecuteRestoreAsync(ServerUrl, Username, Password, FileSystem.AppDataDirectory, FileSystem.CacheDirectory);
                // 覆盖完成，必须强杀或提示重启，否则 SQLite 缓存死锁
                await Shell.Current.DisplayAlertAsync("换心手术成功！", "数据恢复已完成！\n为了让底层数据库重新加载，应用即将强制退出。请在退出后手动重新打开 MonoRead。", "好的，去重启");
                System.Diagnostics.Process.GetCurrentProcess().Kill();
            }
            catch (Exception ex)
            {
                TestResultText = "恢复失败"; TestResultColor = Colors.Red;
                await Shell.Current.DisplayAlertAsync("报错", ex.Message, "确定");
            }
            finally { IsBusy = false; }
        }

        [RelayCommand]
        private async Task GoBackAsync() => await Shell.Current.GoToAsync("..");
    }
}