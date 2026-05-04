using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonoRead.Core.Entities;
using MonoRead.Core.Interfaces;
using MonoRead.Infrastructure.Logging;
using System.Collections.ObjectModel;

namespace MonoRead.App.ViewModels
{
    // 【核心修复】：接收上一级页面传来的 BookId 和 BookTitle
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

        // 当路由参数 BookIdString 被赋值时，自动触发查询
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
                // 调用仓储层按书籍拉取数据
                var notes = await _noteRepository.GetNotesByBookIdAsync(bookId);

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    NotesList.Clear();
                    foreach (var note in notes)
                    {
                        NotesList.Add(note);
                    }
                });
            }
            catch (Exception ex)
            {
                LocalLogger.LogError($"加载笔记异常: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        // 【核心修复】：修复返回按钮失效的问题
        [RelayCommand]
        private async Task GoBackAsync()
        {
            // ".." 代表出栈，回到上一级
            await Shell.Current.GoToAsync("..");
        }
    }
}