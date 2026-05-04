using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonoRead.Core.Entities;
using MonoRead.Core.Interfaces;
using MonoRead.Infrastructure.Logging;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace MonoRead.App.ViewModels
{
    public partial class TrashViewModel : ObservableObject
    {
        private readonly IBookRepository _bookRepository;
        private readonly IBookNoteRepository _noteRepository;

        [ObservableProperty] private ObservableCollection<Book> _deletedBooks = new();
        [ObservableProperty] private ObservableCollection<BookNote> _deletedNotes = new();

        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private bool _isBookTabActive = true;
        [ObservableProperty] private bool _isNoteTabActive = false;

        public TrashViewModel(IBookRepository bookRepository, IBookNoteRepository noteRepository)
        {
            _bookRepository = bookRepository;
            _noteRepository = noteRepository;
        }

        public async Task LoadTrashDataAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                var books = await _bookRepository.GetDeletedBooksAsync();
                var deletedNotes = await _noteRepository.GetDeletedNotesAsync();
                var orphanNotes = await _noteRepository.GetOrphanNotesAsync();

                var combinedNotes = deletedNotes.Concat(orphanNotes)
                                                .OrderByDescending(n => n.UpdatedAt)
                                                .ToList();

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    DeletedBooks.Clear(); foreach (var b in books) DeletedBooks.Add(b);
                    DeletedNotes.Clear(); foreach (var n in combinedNotes) DeletedNotes.Add(n);
                });
            }
            catch (Exception ex) { LocalLogger.LogError($"加载回收站失败: {ex.Message}"); }
            finally { IsBusy = false; }
        }

        [RelayCommand]
        private void SwitchTab(string tabName)
        {
            IsBookTabActive = tabName == "Books";
            IsNoteTabActive = tabName == "Notes";
        }

        // ================= 书籍操作 =================
        [RelayCommand]
        private async Task RestoreBookAsync(Book book)
        {
            if (book == null) return;
            IsBusy = true;
            try
            {
                await _bookRepository.RestoreBookAsync(book.Id);

                // 【核心修复】：强制主线程操作 UI 集合
                MainThread.BeginInvokeOnMainThread(() => DeletedBooks.Remove(book));

                await Application.Current.MainPage!.DisplayAlert("已恢复", $"《{book.Title}》已重返书架", "确定");
                await LoadTrashDataAsync();
            }
            catch (Exception ex)
            {
                await Application.Current.MainPage!.DisplayAlert("错误", $"恢复失败: {ex.Message}", "确定");
            }
            finally { IsBusy = false; }
        }

        [RelayCommand]
        private async Task HardDeleteBookAsync(Book book)
        {
            if (book == null) return;

            bool hasNotes = await _bookRepository.HasActiveNotesAsync(book.Id);
            bool destroyNotes = false;

            if (hasNotes)
            {
                string action = await Application.Current.MainPage!.DisplayActionSheet(
                    $"彻底销毁《{book.Title}》？此操作不可逆！\n该书包含您的读书笔记：",
                    "取消", "彻底销毁书籍及所有笔记", "仅销毁书籍（笔记保留为未分类）");

                if (action == "仅销毁书籍（笔记保留为未分类）") destroyNotes = false;
                else if (action == "彻底销毁书籍及所有笔记") destroyNotes = true;
                else return;
            }
            else
            {
                bool confirm = await Application.Current.MainPage!.DisplayAlert("终极警告", $"确定彻底销毁《{book.Title}》吗？", "销毁", "取消");
                if (!confirm) return;
            }

            IsBusy = true;
            try
            {
                string? filePath = await _bookRepository.PermanentlyDeleteBookAsync(book.Id, destroyNotes);
                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                {
                    File.Delete(filePath);
                }

                // 【核心修复】：强制主线程操作 UI 集合
                MainThread.BeginInvokeOnMainThread(() => DeletedBooks.Remove(book));

                if (!destroyNotes) await LoadTrashDataAsync();
            }
            catch (Exception ex)
            {
                await Application.Current.MainPage!.DisplayAlert("错误", $"销毁失败: {ex.Message}", "确定");
            }
            finally { IsBusy = false; }
        }

        // ================= 笔记操作 =================
        [RelayCommand]
        private async Task RestoreNoteAsync(BookNote note)
        {
            try
            {
                await _noteRepository.RestoreNoteAsync(note.Id);
                // 【核心修复】：强制主线程
                MainThread.BeginInvokeOnMainThread(() => DeletedNotes.Remove(note));
            }
            catch (Exception ex)
            {
                await Application.Current.MainPage!.DisplayAlert("错误", $"恢复失败: {ex.Message}", "确定");
            }
        }

        [RelayCommand]
        private async Task HardDeleteNoteAsync(BookNote note)
        {
            bool confirm = await Application.Current.MainPage!.DisplayAlert("销毁笔记", "彻底删除此条笔记？此操作不可逆。", "销毁", "取消");
            if (confirm)
            {
                try
                {
                    await _noteRepository.PermanentlyDeleteNoteAsync(note.Id);
                    // 【核心修复】：强制主线程
                    MainThread.BeginInvokeOnMainThread(() => DeletedNotes.Remove(note));
                }
                catch (Exception ex)
                {
                    await Application.Current.MainPage!.DisplayAlert("错误", $"销毁失败: {ex.Message}", "确定");
                }
            }
        }

        [RelayCommand] private async Task GoBackAsync() => await Shell.Current.GoToAsync("..");
    }
}