using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonoRead.Core.Entities;
using MonoRead.Core.Interfaces;
using MonoRead.Infrastructure.Logging;
using System.Collections.ObjectModel;
using System.Linq; // 确保引入 Linq

namespace MonoRead.App.ViewModels
{
    public class BookNoteSummary
    {
        // 【核心修复】：与底层实体对齐，改为可空 Guid?，以容纳孤儿笔记分组
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
                // 拉取所有未被软删除的笔记（包含正常笔记和孤儿笔记）
                var allNotes = await _noteRepository.GetAllNotDeletedAsync();

                // 【核心修复】：安全处理 BookId 为 null 的孤儿笔记分组聚合
                var grouped = allNotes.GroupBy(n => n.BookId).Select(g => new BookNoteSummary
                {
                    BookId = g.Key,
                    // 状态机分流：如果 Key 为 null，说明这是失去宿主的孤儿笔记，赋予特定的 UI 标题
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

                // 【核心防御】：处理可空 Guid 的路由传参。如果为空，传递特殊标识 "Orphan"
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