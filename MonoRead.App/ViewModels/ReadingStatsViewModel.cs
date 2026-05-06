using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonoRead.Core.Entities;
using MonoRead.Core.Interfaces;
using MonoRead.Infrastructure.Logging;
using System.Collections.ObjectModel;

namespace MonoRead.App.ViewModels
{
    // 日历格子的 UI 模型
    public class CalendarDayUiModel
    {
        public int Day { get; set; }
        public bool IsEmpty { get; set; }
        public bool HasReadData { get; set; }
        public bool IsToday { get; set; }

        // =========================================================
        // 【核心修复】：使用原生的 Colors.Transparent，消灭黑色煤球
        // =========================================================
        public Color BgColor => HasReadData ? Color.FromArgb("#D1A67B") : Colors.Transparent;
        public Color TextColor => HasReadData ? Colors.White : (IsToday ? Color.FromArgb("#D1A67B") : Color.FromArgb("#333333"));
        public string DisplayText => IsEmpty ? "" : Day.ToString();
    }

    public partial class ReadingStatsViewModel : ObservableObject
    {
        private readonly IReadingRecordRepository _recordRepository;
        private List<ReadingRecord> _allRecords = new();

        private DateTime _currentCursor; // 当前正在查看的月份

        [ObservableProperty] private string _currentMonthYearDisplay = string.Empty;
        [ObservableProperty] private ObservableCollection<CalendarDayUiModel> _calendarDays = new();

        [ObservableProperty] private int _weekMinutes;
        [ObservableProperty] private int _monthMinutes;
        [ObservableProperty] private int _yearMinutes;

        public ReadingStatsViewModel(IReadingRecordRepository recordRepository)
        {
            _recordRepository = recordRepository;
            _currentCursor = DateTime.Today;
        }

        public async Task LoadDataAsync()
        {
            try
            {
                // 一次性拉取所有记录到内存，方便快速切换月份
                _allRecords = await _recordRepository.GetAllAsync() ?? new List<ReadingRecord>();
                RefreshDashboard();
                RefreshCalendar();
            }
            catch (Exception ex) { LocalLogger.LogError($"拉取统计失败: {ex.Message}"); }
        }

        private void RefreshDashboard()
        {
            var today = DateTime.Today;

            // 计算本周一的日期
            int diff = (7 + (today.DayOfWeek - DayOfWeek.Monday)) % 7;
            var startOfWeek = today.AddDays(-1 * diff).Date;

            // 分别计算当周、当月、当年的秒数总和
            long weekSec = _allRecords.Where(r => r.RecordDate.Date >= startOfWeek).Sum(r => r.DurationSeconds);
            long monthSec = _allRecords.Where(r => r.RecordDate.Year == today.Year && r.RecordDate.Month == today.Month).Sum(r => r.DurationSeconds);
            long yearSec = _allRecords.Where(r => r.RecordDate.Year == today.Year).Sum(r => r.DurationSeconds);

            WeekMinutes = (int)(weekSec / 60);
            MonthMinutes = (int)(monthSec / 60);
            YearMinutes = (int)(yearSec / 60);
        }

        private void RefreshCalendar()
        {
            CurrentMonthYearDisplay = $"{_currentCursor.Year}年{_currentCursor.Month}月";

            var daysList = new List<CalendarDayUiModel>();
            var firstDayOfMonth = new DateTime(_currentCursor.Year, _currentCursor.Month, 1);
            int daysInMonth = DateTime.DaysInMonth(_currentCursor.Year, _currentCursor.Month);

            // 0=周日, 1=周一。中国习惯日历从周一开始排，所以要做个偏移
            int startDayOfWeek = (int)firstDayOfMonth.DayOfWeek;
            int emptySlots = startDayOfWeek == 0 ? 6 : startDayOfWeek - 1;

            // 1. 填充月首空白格
            for (int i = 0; i < emptySlots; i++)
            {
                daysList.Add(new CalendarDayUiModel { IsEmpty = true });
            }

            // 2. 填充真实日期
            for (int i = 1; i <= daysInMonth; i++)
            {
                var targetDate = new DateTime(_currentCursor.Year, _currentCursor.Month, i);

                bool hasData = _allRecords.Any(r => r.RecordDate.Date == targetDate && r.DurationSeconds > 60); // 读超过1分钟才算打卡
                bool isToday = targetDate == DateTime.Today;

                daysList.Add(new CalendarDayUiModel
                {
                    Day = i,
                    IsEmpty = false,
                    HasReadData = hasData,
                    IsToday = isToday
                });
            }

            CalendarDays = new ObservableCollection<CalendarDayUiModel>(daysList);
        }

        [RelayCommand]
        private void PreviousMonth()
        {
            _currentCursor = _currentCursor.AddMonths(-1);
            RefreshCalendar();
        }

        [RelayCommand]
        private void NextMonth()
        {
            // 防止看到未来的月份
            if (_currentCursor.Year == DateTime.Today.Year && _currentCursor.Month == DateTime.Today.Month) return;

            _currentCursor = _currentCursor.AddMonths(1);
            RefreshCalendar();
        }

        [RelayCommand]
        private async Task GoBackAsync() => await Shell.Current.GoToAsync("..");
    }
}