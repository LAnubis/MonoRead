using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonoRead.Core.Entities;
using MonoRead.Core.Interfaces;
using System.Collections.ObjectModel;

namespace MonoRead.App.ViewModels
{
    // =========================================================
    // 【新增】：专为“最近阅读”打造的 UI 展示模型，解决时间显示和时区问题
    // =========================================================
    public partial class RecentBookUiNode : ObservableObject
    {
        public Book OriginalBook { get; set; }

        // 映射 UI 需要展示的基础属性
        public string Title => OriginalBook.Title;
        public string ProgressText => OriginalBook.ProgressText;

        // 【核心修复 2】：人性化时间展示，附带本地时区矫正
        public string DisplayLastReadTime
        {
            get
            {
                // 强制转换为手机当前的本地时区
                DateTime localTime = OriginalBook.UpdatedAt.ToLocalTime();

                // 人性化显示格式
                if (localTime.Date == DateTime.Today)
                {
                    return $"今天 {localTime:HH:mm}";
                }
                else if (localTime.Date == DateTime.Today.AddDays(-1))
                {
                    return $"昨天 {localTime:HH:mm}";
                }
                else
                {
                    return localTime.ToString("MM-dd HH:mm");
                }
            }
        }
    }

    public partial class RecentViewModel : ObservableObject
    {
        private readonly IBookRepository _bookRepository;

        // 【核心修改】：绑定的集合类型从 Book 改为了包装后的 RecentBookUiNode
        [ObservableProperty]
        private ObservableCollection<RecentBookUiNode> _recentBooks = new();

        [ObservableProperty]
        private bool _isBusy;

        public RecentViewModel(IBookRepository bookRepository)
        {
            _bookRepository = bookRepository;
        }

        [RelayCommand]
        public async Task LoadRecentBooksAsync()
        {
            if (IsBusy) return;
            IsBusy = true;

            try
            {
                var recentUiNodes = await Task.Run(async () =>
                {
                    // 【核心修复】：接入我们在书架定义的同一个交警（全局锁）
                    await LibraryViewModel.GlobalDbLock.WaitAsync();
                    try
                    {
                        var allBooks = await _bookRepository.GetAllBooksAsync();

                        DateTime localTime = DateTime.UtcNow.ToLocalTime();
                        var limitDate = localTime.AddDays(-3);

                        return allBooks
                            .Where(b => !b.IsDeleted && b.UpdatedAt >= limitDate)
                            .OrderByDescending(b => b.UpdatedAt)
                            .Select(b => new RecentBookUiNode { OriginalBook = b })
                            .ToList();
                    }
                    finally
                    {
                        LibraryViewModel.GlobalDbLock.Release();
                    }
                });

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    // 【核心修复】：整体赋值替换！拒绝 Clear 和 Foreach Add 带来的 UI 撕裂和闪退
                    RecentBooks = new ObservableCollection<RecentBookUiNode>(recentUiNodes);
                });
            }
            catch (Exception ex)
            {
                // 吞掉异常防止 App 崩溃，打印日志备查
                System.Diagnostics.Debug.WriteLine($"加载最近阅读失败: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task OpenBookAsync(RecentBookUiNode node)
        {
            if (node?.OriginalBook == null) return;
            string route = $"{nameof(Views.ReaderPage)}?BookId={node.OriginalBook.Id}";
            await Shell.Current.GoToAsync(route);
        }
    }
}