using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonoRead.Infrastructure.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace MonoRead.App.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        [RelayCommand]
        private async Task GoToTrashAsync()
        {
            try { await Shell.Current.GoToAsync("TrashPage"); }
            catch (Exception) { await Application.Current.MainPage.DisplayAlert("提示", "回收站页面未就绪", "确定"); }
        }

        [RelayCommand]
        private async Task ExportLogAsync()
        {
            try
            {
                string fileName = $"monoread_{DateTime.Now:yyyyMMdd}.log";
                string filePath = System.IO.Path.Combine(LocalLogger.LogDirectory, fileName);

                if (System.IO.File.Exists(filePath))
                {
                    await Share.Default.RequestAsync(new ShareFileRequest
                    {
                        Title = "导出 MonoRead 崩溃日志",
                        File = new ShareFile(filePath)
                    });
                }
                else
                {
                    await Application.Current.MainPage.DisplayAlert("提示", "今日尚无报错日志产生", "确定");
                }
            }
            catch (Exception ex)
            {
                LocalLogger.LogError($"导出日志失败: {ex.Message}");
                await Application.Current.MainPage.DisplayAlert("导出失败", ex.Message, "确定");
            }
        }
    }
}
