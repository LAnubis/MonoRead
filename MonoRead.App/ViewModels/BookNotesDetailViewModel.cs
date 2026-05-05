using CommunityToolkit.Maui.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonoRead.Core.Entities;
using MonoRead.Core.Interfaces;
using MonoRead.Infrastructure.Logging;
using System.Collections.ObjectModel;

namespace MonoRead.App.ViewModels
{
    // 【节点 UI 模型】：管理折叠展开状态与复选框状态
    public partial class NoteItemUiModel : ObservableObject
    {
        public BookNote Note { get; set; }

        public bool HasComment => !string.IsNullOrWhiteSpace(Note.UserComment);

        [ObservableProperty] private bool _isExpanded;
        [ObservableProperty] private bool _isSelected;

        [RelayCommand]
        private void ToggleExpand()
        {
            // 如果有想法，点击触发折叠/展开；如果没有想法，点击无反应
            if (HasComment) IsExpanded = !IsExpanded;
        }

        [RelayCommand]
        private void ToggleSelection()
        {
            IsSelected = !IsSelected;
        }
    }

    // 【分组容器】：用于 CollectionView 的 IsGrouped="True"
    public class NoteGroup : ObservableCollection<NoteItemUiModel>
    {
        public string ChapterTitle { get; private set; }
        public NoteGroup(string chapterTitle, IEnumerable<NoteItemUiModel> items) : base(items)
        {
            ChapterTitle = chapterTitle;
        }
    }

    [QueryProperty(nameof(BookIdString), "BookId")]
    [QueryProperty(nameof(BookTitle), "BookTitle")]
    public partial class BookNotesDetailViewModel : ObservableObject
    {
        private readonly IBookNoteRepository _noteRepository;
        private readonly IBookRepository _bookRepository;

        [ObservableProperty] private string _bookIdString = string.Empty;
        [ObservableProperty] private string _bookTitle = string.Empty;
        [ObservableProperty] private bool _isBusy;

        // 分组数据源
        [ObservableProperty] private ObservableCollection<NoteGroup> _groupedNotes = new();

        // 管理模式状态
        [ObservableProperty] private bool _isManageMode;
        [ObservableProperty] private string _manageButtonText = "管理";

        public BookNotesDetailViewModel(IBookNoteRepository noteRepository, IBookRepository bookRepository)
        {
            _noteRepository = noteRepository;
            _bookRepository = bookRepository;
        }

        partial void OnBookIdStringChanged(string value)
        {
            if (value == "Orphan") LoadOrphanNotesAsync();
            else if (Guid.TryParse(value, out Guid parsedId)) LoadBookNotesAsync(parsedId);
        }

        private async void LoadBookNotesAsync(Guid bookId)
        {
            IsBusy = true;
            try
            {
                var book = await _bookRepository.GetBookWithChaptersAsync(bookId);
                var notes = await _noteRepository.GetNotesByBookIdAsync(bookId);
                var activeNotes = notes.Where(n => !n.IsDeleted && !n.IsOrphan).OrderBy(n => n.CreatedAt).ToList();

                var groupedByChapter = activeNotes.GroupBy(n => n.ChapterId);
                var groupedResult = new List<NoteGroup>();

                foreach (var group in groupedByChapter)
                {
                    var chapter = book?.Chapters.FirstOrDefault(c => c.Id == group.Key);
                    string chapterName = chapter != null ? chapter.Title : "全局笔记";

                    var uiItems = group.Select(n => new NoteItemUiModel { Note = n, IsExpanded = false, IsSelected = false });
                    groupedResult.Add(new NoteGroup(chapterName, uiItems));
                }

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    GroupedNotes.Clear();
                    foreach (var g in groupedResult) GroupedNotes.Add(g);
                });
            }
            catch (Exception ex) { LocalLogger.LogError($"加载笔记异常: {ex.Message}"); }
            finally { IsBusy = false; }
        }

        private async void LoadOrphanNotesAsync()
        {
            // 孤儿笔记不分组章节，直接拉取展示
            IsBusy = true;
            try
            {
                var allNotes = await _noteRepository.GetAllNotDeletedAsync();
                var orphans = allNotes.Where(n => n.BookId == null || n.IsOrphan).OrderByDescending(n => n.CreatedAt).ToList();

                var uiItems = orphans.Select(n => new NoteItemUiModel { Note = n, IsExpanded = false, IsSelected = false });
                var group = new NoteGroup("已删除书籍的笔记", uiItems);

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    GroupedNotes.Clear();
                    GroupedNotes.Add(group);
                });
            }
            catch (Exception ex) { LocalLogger.LogError($"加载孤儿笔记异常: {ex.Message}"); }
            finally { IsBusy = false; }
        }

        [RelayCommand]
        private void ToggleManageMode()
        {
            IsManageMode = !IsManageMode;
            ManageButtonText = IsManageMode ? "完成" : "管理";

            // 退出管理模式时，清空所有勾选
            if (!IsManageMode)
            {
                foreach (var group in GroupedNotes)
                    foreach (var item in group)
                        item.IsSelected = false;
            }
        }

        [RelayCommand]
        private async Task DeleteSelectedNotesAsync()
        {
            var selectedNotes = GroupedNotes.SelectMany(g => g).Where(i => i.IsSelected).Select(i => i.Note).ToList();
            if (!selectedNotes.Any()) return;

            bool confirm = await Application.Current.MainPage!.DisplayAlert("确认删除", $"确定要删除选中的 {selectedNotes.Count} 条笔记吗？", "删除", "取消");
            if (!confirm) return;

            IsBusy = true;
            try
            {
                foreach (var note in selectedNotes)
                {
                    await _noteRepository.DeleteAsync(note);
                }

                ToggleManageModeCommand.Execute(null);

                // 重新加载刷新列表
                if (BookIdString == "Orphan") LoadOrphanNotesAsync();
                else if (Guid.TryParse(BookIdString, out Guid id)) LoadBookNotesAsync(id);
            }
            catch (Exception ex)
            {
                await Application.Current.MainPage!.DisplayAlert("错误", $"删除失败: {ex.Message}", "确定");
            }
            finally { IsBusy = false; }
        }

        [RelayCommand]
        private async Task GoBackAsync() => await Shell.Current.GoToAsync("..");
    }
}