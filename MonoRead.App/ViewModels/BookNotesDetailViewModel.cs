using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonoRead.Core.Entities;
using MonoRead.Core.Interfaces;
using MonoRead.Infrastructure.Logging; // 确保引入日志
using System.Collections.ObjectModel;

namespace MonoRead.App.ViewModels
{
    [QueryProperty(nameof(BookIdString), "BookId")]
    [QueryProperty(nameof(BookTitle), "BookTitle")]
    public partial class BookNotesDetailViewModel : ObservableObject
    {
        private readonly IBookNoteRepository _noteRepository;

        [ObservableProperty]
        private string _bookIdString = string.Empty;

        [ObservableProperty]
        private string _bookTitle = "笔记详情";

        [ObservableProperty]
        private ObservableCollection<BookNote> _notesList = new();

        [ObservableProperty]
        private bool _isBusy;

        public BookNotesDetailViewModel(IBookNoteRepository noteRepository)
        {
            _noteRepository = noteRepository;
        }

        partial void OnBookIdStringChanged(string value)
        {
            if (Guid.TryParse(value, out Guid bookId))
            {
                LoadNotesAsync(bookId);
            }
        }

        private async void LoadNotesAsync(Guid bookId)
        {
            if (IsBusy) return;
            IsBusy = true;

            try
            {
                // 1. 异步请求数据库 (非 UI 线程)
                var notes = await _noteRepository.GetNotesByBookIdAsync(bookId);

                // 2. 将数据装入新的 ObservableCollection，准备进行内存替换
                var newCollection = new ObservableCollection<BookNote>(notes);

                // 3. 切回主线程进行 UI 渲染
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    // 【核心渲染修复】：直接替换集合实例，只触发 1 次重绘，彻底解决 CollectionView 循环 Add 导致的 Android 崩溃
                    NotesList = newCollection;
                });
            }
            catch (Exception ex)
            {
                // 【核心架构修复】：捕获原本会导致 async void 闪退的幽灵异常！
                LocalLogger.LogError($"加载笔记详情失败: {ex.Message}");

                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    // 将死因弹窗显示给开发者/用户
                    await Application.Current.MainPage!.DisplayAlert("数据加载异常", ex.Message, "确定");
                });
            }
            finally
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    IsBusy = false;
                });
            }
        }

        [RelayCommand]
        private async Task GoBackAsync()
        {
            await Shell.Current.GoToAsync("..");
        }
    }
}