using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonoRead.Core.Entities;
using MonoRead.Core.Interfaces;
using MonoRead.Infrastructure.Logging;
using System.Collections.ObjectModel;
using System.Linq;

namespace MonoRead.App.ViewModels
{
    public class BookNoteSummary
    {
        // 容纳孤儿笔记分组
        public Guid? BookId { get; set; }
        public string BookTitle { get; set; } = string.Empty;
        public int NoteCount { get; set; }
        public DateTime LatestNoteTime { get; set; }
    }

    public partial class NotesViewModel : ObservableObject
    {
        private readonly IBookNoteRepository _noteRepository;

        [ObservableProperty]
        private ObservableCollection<BookNoteSummary> _notedBooks = new();

        [ObservableProperty]
        private bool _isBusy;

        public NotesViewModel(IBookNoteRepository noteRepository)
        {
            _noteRepository = noteRepository;
        }

        [RelayCommand]
        public async Task LoadNotedBooksAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                var allNotes = await _noteRepository.GetAllNotDeletedAsync();

                var grouped = allNotes.GroupBy(n => n.BookId).Select(g => new BookNoteSummary
                {
                    BookId = g.Key,
                    BookTitle = g.Key == null ? "未分类(孤儿)笔记" : (g.FirstOrDefault()?.BookTitle ?? "未知书籍"),
                    NoteCount = g.Count(),
                    LatestNoteTime = g.Max(n => n.CreatedAt)
                }).OrderByDescending(s => s.LatestNoteTime).ToList();

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    NotedBooks.Clear();
                    foreach (var item in grouped) NotedBooks.Add(item);
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

        [RelayCommand]
        private async Task GoToBookNotesAsync(BookNoteSummary summary)
        {
            try
            {
                if (summary == null) return;
                string bookIdStr = summary.BookId.HasValue ? summary.BookId.Value.ToString() : "Orphan";
                await Shell.Current.GoToAsync($"BookNotesDetailPage?BookId={bookIdStr}&BookTitle={Uri.EscapeDataString(summary.BookTitle)}");
            }
            catch (Exception ex)
            {
                LocalLogger.LogError($"跳转笔记详情异常: {ex.Message}");
            }
        }
    }
}