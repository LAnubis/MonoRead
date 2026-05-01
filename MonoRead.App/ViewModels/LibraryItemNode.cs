using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Text;

namespace MonoRead.App.ViewModels
{
    // 【架构核心】：统一包装 Book 和 Folder，解决 CollectionView 只能绑定一种类型的问题
    public partial class LibraryItemNode : ObservableObject
    {
        public bool IsFolder { get; set; }
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;

        // 携带底层原始实体，方便操作
        public object? OriginalEntity { get; set; }

        [ObservableProperty]
        private bool _isSelected;
        // 【核心字段解答】：专门控制 UI 层复选框是否显示的聚合状态属性
        // 它的值由 ViewModel 统一计算：仅在“是编辑模式”且“不是文件夹”时为 true。
        [ObservableProperty]
        private bool _showCheckBox;
    }
}
