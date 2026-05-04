using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonoRead.Core.Entities;
using MonoRead.Core.Interfaces;
using MonoRead.Infrastructure.Logging;
using System.Collections.ObjectModel;

namespace MonoRead.App.ViewModels
{
    public class BookNoteSummary
    {
        public Guid BookId { get; set; }
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
                // 正式从数据库物理表读取所有未删除的笔记
                var allNotes = await _noteRepository.GetAllNotDeletedAsync();

                // 按书籍进行聚合，展示数量和最近时间
                var grouped = allNotes.GroupBy(n => n.BookId).Select(g => new BookNoteSummary
                {
                    BookId = g.Key,
                    BookTitle = g.FirstOrDefault()?.BookTitle ?? "未知书籍",
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
                await Shell.Current.GoToAsync($"BookNotesDetailPage?BookId={summary.BookId}&BookTitle={Uri.EscapeDataString(summary.BookTitle)}");
            }
            catch (Exception ex)
            {
                LocalLogger.LogError($"加载笔记异常: {ex.Message}");
            }
          
        }
    }
}