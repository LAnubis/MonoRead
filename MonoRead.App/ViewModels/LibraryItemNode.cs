using CommunityToolkit.Mvvm.ComponentModel;
using MonoRead.Core.Entities;
using Microsoft.Maui.Graphics;

namespace MonoRead.App.ViewModels
{
    // 【架构核心】：统一包装 Book 和 Folder，解决 CollectionView 只能绑定一种类型的问题
    public partial class LibraryItemNode : ObservableObject
    {
        public bool IsFolder { get; set; }
        public Guid Id { get; set; }

        [ObservableProperty]
        private string _title = string.Empty;

        // 【核心修复 1】：升级为可观察属性，配合后续的进度动态刷新
        [ObservableProperty]
        private string _subtitle = string.Empty;

        // 携带底层原始实体，方便操作
        public object? OriginalEntity { get; set; }

        [ObservableProperty]
        private bool _isSelected;

        [ObservableProperty]
        private bool _showCheckBox;

        // ==========================================
        // 【新增】：云端/本地 状态徽章属性
        // ==========================================
        public bool IsCloudBook { get; set; }

        [ObservableProperty]
        private string _statusBadgeText = string.Empty;

        [ObservableProperty]
        private Color _badgeBackgroundColor = Colors.Transparent;

        [ObservableProperty]
        private Color _badgeTextColor = Colors.Transparent;

        // 【能力】：供 ViewModel 在页面 OnAppearing 时主动唤醒刷新进度
        public void RefreshProgress()
        {
            if (OriginalEntity is Book book)
            {
                Subtitle = book.ProgressText;
            }
        }
    }
}