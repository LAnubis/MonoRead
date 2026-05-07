using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonoRead.Core.Interfaces;
using MonoRead.Infrastructure.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace MonoRead.App.ViewModels
{
    // 绿点模型
    public partial class HeatmapBox : ObservableObject
    {
        public DateTime Date { get; set; }
        public int DurationSeconds { get; set; }
        public string ColorHex { get; set; } = "#EBEDF0"; // 默认浅灰色（未阅读）
        public string Tooltip => $"{Date:MM-dd}: 阅读 {DurationSeconds / 60} 分钟";
    }
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly IReadingRecordRepository _recordRepository;

        [ObservableProperty] private int _totalReadMinutes = 0;
        [ObservableProperty] private int _continuousReadDays = 0;

        // 绑定的热力图数据源
        [ObservableProperty] private ObservableCollection<HeatmapBox> _heatBoxes = new();

        // 显示在设置页面的总时长（你可以每次 OnAppearing 的时候去刷新这个值）
        [ObservableProperty]
        private string _totalReadDisplay = "0分钟";
        // =========================================================
        // 【新增】：阅读模式开关 (默认 false = 翻页模式)
        // =========================================================
        [ObservableProperty]
        private bool _isScrollMode = Preferences.Default.Get("IsScrollMode", false);

        public SettingsViewModel(IReadingRecordRepository recordRepository)
        {
            _recordRepository = recordRepository;
            LoadStatisticsAsync();
        }
        [RelayCommand]
        private async Task GoToReadingStatsAsync()
        {
            // 使用 Shell 路由进行页面跳转
            await Shell.Current.GoToAsync(nameof(Views.ReadingStatsPage));
        }
        partial void OnIsScrollModeChanged(bool value)
        {
            // 当开关拨动时，立刻保存到本地
            Preferences.Default.Set("IsScrollMode", value);
        }
        private async void LoadStatisticsAsync()
        {
            // 获取过去 90 天的数据（适合手机横向显示）
            var records = await _recordRepository.GetRecentRecordsAsync(90);

            int totalSecs = 0;
            int streak = 0;
            DateTime today = DateTime.UtcNow.Date;

            // 计算总时长和连续阅读天数
            foreach (var r in records.OrderByDescending(x => x.RecordDate))
            {
                totalSecs += r.DurationSeconds;
                // 简单的连续打卡计算逻辑
                if (r.RecordDate == today.AddDays(-streak) && r.DurationSeconds > 0)
                    streak++;
            }

            TotalReadMinutes = totalSecs / 60;
            ContinuousReadDays = streak;

            // 绘制热力图矩阵 (补齐 90 天的格子，没有记录的填灰色)
            var boxes = new List<HeatmapBox>();
            for (int i = 89; i >= 0; i--)
            {
                var targetDate = today.AddDays(-i);
                var record = records.FirstOrDefault(r => r.RecordDate == targetDate);
                int secs = record?.DurationSeconds ?? 0;
                int mins = secs / 60;

                string color = "#EBEDF0"; // 0分钟: 灰色
                if (mins > 0 && mins <= 15) color = "#9BE9A8"; // 浅绿
                else if (mins > 15 && mins <= 45) color = "#40C463"; // 中绿
                else if (mins > 45 && mins <= 90) color = "#30A14E"; // 深绿
                else if (mins > 90) color = "#216E39"; // 墨绿 (大佬级别)

                boxes.Add(new HeatmapBox { Date = targetDate, DurationSeconds = secs, ColorHex = color });
            }

            MainThread.BeginInvokeOnMainThread(() =>
            {
                HeatBoxes.Clear();
                foreach (var box in boxes) HeatBoxes.Add(box);
            });
        }

        [RelayCommand]
        private async Task GoToTrashAsync()
        {
            try { await Shell.Current.GoToAsync("TrashPage"); }
            catch (Exception) { await Shell.Current.DisplayAlertAsync("提示", "回收站页面未就绪", "确定"); }
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
                    await Shell.Current.DisplayAlertAsync("提示", "今日尚无报错日志产生", "确定");
                }
            }
            catch (Exception ex)
            {
                LocalLogger.LogError($"导出日志失败: {ex.Message}");
                await Shell.Current.DisplayAlertAsync("导出失败", ex.Message, "确定");
            }
        }


        [RelayCommand]
        private async Task GoToAboutUsAsync()
        {
            try
            {
                await Shell.Current.GoToAsync("AboutUsPage");
            }
            catch (Exception ex)
            {
                LocalLogger.LogError($"跳转关于页面失败: {ex.Message}");
            }
        }
        [RelayCommand]
        private async Task GoToCloudBackupAsync()
        {
            try
            {
                await Shell.Current.GoToAsync("CloudBackupPage");
            }
            catch (Exception ex)
            {
                LocalLogger.LogError($"跳转云端配置页面失败: {ex.Message}");
            }
        }

        public async Task LoadTotalReadingTimeAsync()
        {
            try
            {
                var records = await _recordRepository.GetAllAsync();
                if (records != null && records.Any())
                {
                    long totalSeconds = records.Sum(r => r.DurationSeconds);
                    int hours = (int)(totalSeconds / 3600);
                    int minutes = (int)((totalSeconds % 3600) / 60);

                    if (hours > 0)
                        TotalReadDisplay = $"{hours}小时{minutes}分钟";
                    else
                        TotalReadDisplay = $"{minutes}分钟";
                }
            }
            catch (Exception)
            {
                TotalReadDisplay = "获取失败";
            }
        }

    }
}
