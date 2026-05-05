using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonoRead.Core.Interfaces;

namespace MonoRead.App.ViewModels
{
    public partial class CloudBackupViewModel : ObservableObject
    {
        private readonly ICloudStorageService _cloudStorageService;

        [ObservableProperty] private string _serverUrl = "https://dav.jianguoyun.com/dav/";
        [ObservableProperty] private string _username = string.Empty;
        [ObservableProperty] private string _password = string.Empty; // 应用授权码

        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private string _testResultText = "暂未测试";
        [ObservableProperty] private Color _testResultColor = Colors.Gray;

        public CloudBackupViewModel(ICloudStorageService cloudStorageService)
        {
            _cloudStorageService = cloudStorageService;
            LoadConfigAsync();
        }

        private async void LoadConfigAsync()
        {
            try
            {
                var savedUrl = await SecureStorage.Default.GetAsync("WebDav_Url");
                var savedUser = await SecureStorage.Default.GetAsync("WebDav_User");
                var savedPass = await SecureStorage.Default.GetAsync("WebDav_Pass");

                if (!string.IsNullOrEmpty(savedUrl)) ServerUrl = savedUrl;
                if (!string.IsNullOrEmpty(savedUser)) Username = savedUser;
                if (!string.IsNullOrEmpty(savedPass)) Password = savedPass;
            }
            catch { /* 处理凭据存储异常 */ }
        }

        [RelayCommand]
        private async Task SaveAndTestAsync()
        {
            if (string.IsNullOrWhiteSpace(ServerUrl) || string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
            {
                TestResultText = "请填写完整配置！";
                TestResultColor = Colors.Red;
                return;
            }

            if (IsBusy) return;
            IsBusy = true;
            TestResultText = "正在连接坚果云服务器...";
            TestResultColor = Colors.Orange;

            try
            {
                bool isSuccess = await _cloudStorageService.TestConnectionAsync(ServerUrl, Username, Password);
                if (isSuccess)
                {
                    TestResultText = "🎉 连接成功！配置已安全保存。";
                    TestResultColor = Colors.Green;

                    // 测试成功才写入本地安全存储
                    await SecureStorage.Default.SetAsync("WebDav_Url", ServerUrl);
                    await SecureStorage.Default.SetAsync("WebDav_User", Username);
                    await SecureStorage.Default.SetAsync("WebDav_Pass", Password);
                }
                else
                {
                    TestResultText = "❌ 连接失败，请检查账号、密码(应用授权码)是否正确。";
                    TestResultColor = Colors.Red;
                }
            }
            catch (Exception)
            {
                TestResultText = "网络异常或请求超时";
                TestResultColor = Colors.Red;
            }
            finally { IsBusy = false; }
        }

        [RelayCommand]
        private async Task GoBackAsync() => await Shell.Current.GoToAsync("..");
    }
}
