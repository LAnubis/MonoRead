#if ANDROID
using Android.Views;
using Android.Widget;
using Microsoft.Maui.ApplicationModel;
using CommunityToolkit.Mvvm.Messaging;

namespace MonoRead.App.Platforms.Android;

// 定义跨域传输的选中文字消息
public record TextSelectedMessage(string SelectedText);

public class MonoReadActionModeCallback : Java.Lang.Object, ActionMode.ICallback
{
    private readonly TextView _textView;

    public MonoReadActionModeCallback(TextView textView)
    {
        _textView = textView;
    }

    public bool OnCreateActionMode(ActionMode mode, IMenu menu)
    {
        // 1. 净化：清空系统自带的（全选、Web搜索、分享等杂乱功能）
        menu?.Clear();

        // 2. 注入：符合产品 MVP 定义的三个极简操作
        menu?.Add(0, 1, 0, "划线");
        menu?.Add(0, 2, 1, "写笔记");
        menu?.Add(0, 3, 2, "复制");
        return true; // 返回 true 表示拦截成功
    }

    public bool OnPrepareActionMode(ActionMode mode, IMenu menu) => false;

    public bool OnActionItemClicked(ActionMode mode, IMenuItem item)
    {
        if (mode == null || item == null) return false;

        // 获取当前水滴光标真实选中的文字
        int min = System.Math.Max(0, System.Math.Min(_textView.SelectionStart, _textView.SelectionEnd));
        int max = System.Math.Max(0, System.Math.Max(_textView.SelectionStart, _textView.SelectionEnd));
        string selectedText = _textView.TextFormatted?.SubSequenceFormatted(min, max)?.ToString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(selectedText)) return true;

        switch (item.ItemId)
        {
            case 1: // 划线功能 (直接沉淀资产)
                // TODO: 发送静默划线落盘消息
                mode.Finish(); // 关闭系统悬浮条
                return true;

            case 2: // 写笔记 (调起 UI 工作台)
                // 将选中的文字发送回 MAUI 的 ViewModel 层
                WeakReferenceMessenger.Default.Send(new TextSelectedMessage(selectedText));
                mode.Finish();
                return true;

            case 3: // 复制
                Clipboard.Default.SetTextAsync(selectedText);
                mode.Finish();
                return true;

            default:
                return false;
        }
    }

    public void OnDestroyActionMode(ActionMode mode) { }
}
#endif