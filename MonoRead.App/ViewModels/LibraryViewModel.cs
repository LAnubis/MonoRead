using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace MonoRead.App.ViewModels
{
    // 必须是 partial class，因为 Toolkit 会自动帮我们生成底层代码
    public partial class LibraryViewModel : ObservableObject
    {
        // 这个标记会自动生成一个名为 ImportBookCommand 的命令供 XAML 绑定
        [RelayCommand]
        private async Task ImportBookAsync()
        {
            // 临时写一个弹窗测试绑定是否成功
            Debug.WriteLine("导入按钮被点击了！");
            await Shell.Current.DisplayAlert("提示", "文件选择器即将接入！", "期待");
        }
    }
}
