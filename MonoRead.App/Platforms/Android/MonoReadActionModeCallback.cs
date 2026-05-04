#if ANDROID
using Android.Views;
using Android.Widget;
using CommunityToolkit.Mvvm.Messaging;
using MonoRead.App.ViewModels; // 确保跨域消息能被正确找到

namespace MonoRead.App.Platforms.Android;

// ================== 原生文本悬浮菜单劫持器 ==================
public class MonoReadActionModeCallback : Java.Lang.Object, ActionMode.ICallback
{
    private readonly TextView _textView;

    public MonoReadActionModeCallback(TextView textView)
    {
        _textView = textView;
    }

    public bool OnCreateActionMode(ActionMode mode, IMenu menu)
    {
        // 核心拦截：系统一旦决定选词，立马发射消息，告诉 ViewModel 阻断 250ms 的菜单呼出倒计时！
        WeakReferenceMessenger.Default.Send(new TextSelectionStartedMessage());

        menu?.Clear();
        menu?.Add(0, 1, 0, "划线");
        menu?.Add(0, 2, 1, "写笔记");
        menu?.Add(0, 3, 2, "复制");
        return true;
    }

    public bool OnPrepareActionMode(ActionMode mode, IMenu menu) => false;

    public bool OnActionItemClicked(ActionMode mode, IMenuItem item)
    {
        if (mode == null || item == null) return false;

        int start = _textView.SelectionStart;
        int end = _textView.SelectionEnd;

        int min = System.Math.Max(0, System.Math.Min(start, end));
        int max = System.Math.Max(0, System.Math.Max(start, end));

        string fullText = _textView.Text ?? string.Empty;
        string selectedText = string.Empty;

        if (fullText.Length > 0 && max > min && max <= fullText.Length)
        {
            selectedText = fullText.Substring(min, max - min);
        }

        if (string.IsNullOrWhiteSpace(selectedText))
        {
            mode.Finish();
            return true;
        }

        switch (item.ItemId)
        {
            case 1:
                mode.Finish();
                return true;

            case 2: // 写笔记
                WeakReferenceMessenger.Default.Send(new TextSelectedMessage(selectedText));
                mode.Finish();
                return true;

            case 3: // 复制 (修复：增加 DataTransfer 绝对路径)
                global::Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.Default.SetTextAsync(selectedText);
                mode.Finish();
                return true;

            default:
                return false;
        }
    }

    public void OnDestroyActionMode(ActionMode mode) { }
}

// ================== 原生手势精准拦截器 ==================
// 修复：使用 global::Android 强制突破命名空间冲突
public class ReadingTouchListener : Java.Lang.Object, global::Android.Views.View.IOnTouchListener
{
    private readonly GestureDetector _gestureDetector;

    public ReadingTouchListener(global::Android.Content.Context context)
    {
        _gestureDetector = new GestureDetector(context, new ReadingGestureListener());
    }

    public bool OnTouch(global::Android.Views.View v, MotionEvent e)
    {
        // 先让 GestureDetector 审问这个触摸事件，截断原生双击
        if (_gestureDetector.OnTouchEvent(e))
        {
            return true;
        }
        return false;
    }
}

public class ReadingGestureListener : GestureDetector.SimpleOnGestureListener
{
    public override bool OnSingleTapConfirmed(MotionEvent e)
    {
        // 强行指定绝对路径，无视命名空间丢失问题
        CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(
            new global::MonoRead.App.ViewModels.MenuToggleRequestedMessage()
        );
        return true;
    }

    public override bool OnDoubleTap(MotionEvent e)
    {
        // 强行指定绝对路径
        CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(
            new global::MonoRead.App.ViewModels.MenuToggleRequestedMessage()
        );
        return true;
    }
}
#endif