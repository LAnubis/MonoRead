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
            // 防抖：如果正在加载且已经有数据，直接返回，防止切换 Tab 时引发页面闪烁
            if (IsBusy) return;
            IsBusy = true;

            try
            {
                // =========================================================
                // 【核心修复 1】：将所有的数据库拉取、过滤排序逻辑关进后台线程！
                // 彻底释放主线程，让底部 Tab 切换动画恢复丝滑。
                // =========================================================
                var recentUiNodes = await Task.Run(async () =>
                {
                    var allBooks = await _bookRepository.GetAllBooksAsync();

                    DateTime localTime = DateTime.UtcNow.ToLocalTime();
                    var limitDate = localTime.AddDays(-3);

                    var recent = allBooks
                        .Where(b => !b.IsDeleted && b.UpdatedAt >= limitDate)
                        .OrderByDescending(b => b.UpdatedAt)
                        .Select(b => new RecentBookUiNode { OriginalBook = b }) // 穿上 UI 外衣
                        .ToList();

                    return recent;
                });

                // =========================================================
                // 数据就绪后，切回主线程进行极速 UI 渲染
                // =========================================================
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    RecentBooks.Clear();
                    foreach (var node in recentUiNodes)
                    {
                        RecentBooks.Add(node);
                    }
                });
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