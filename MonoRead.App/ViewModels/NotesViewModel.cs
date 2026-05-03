using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonoRead.Core.Entities;
// using MonoRead.Core.Interfaces; // 请注入您的 Repository
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
        [ObservableProperty]
        private ObservableCollection<BookNoteSummary> _notedBooks = new();

        [ObservableProperty]
        private bool _isBusy;

        [RelayCommand]
        public async Task LoadNotedBooksAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                // 模拟数据库获取所有笔记 (请替换为您的 _noteRepository.GetAllAsync())
                var allNotes = GenerateFakeNotes().Where(n => !n.IsDeleted).ToList();

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
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task GoToBookNotesAsync(BookNoteSummary summary)
        {
            if (summary == null) return;
            await Shell.Current.GoToAsync($"BookNotesDetailPage?BookId={summary.BookId}&BookTitle={Uri.EscapeDataString(summary.BookTitle)}");
        }

        // 占位假数据
        private List<BookNote> GenerateFakeNotes() => new List<BookNote>();
    }
}