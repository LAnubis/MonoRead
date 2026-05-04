using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonoRead.Core.Entities;
using MonoRead.Core.Interfaces;
using MonoRead.Infrastructure.Logging;
using System.Collections.ObjectModel;

namespace MonoRead.App.ViewModels
{
    // 【架构隔离】：表现层防腐模型，确保数据库实体 BookNote 不被混入 UI 特有的 IsSelected 属性
    public partial class SelectableBookNote : ObservableObject
    {
        public BookNote Note { get; }

        [ObservableProperty]
        private bool _isSelected;

        public SelectableBookNote(BookNote note)
        {
            Note = note;
        }
    }

    [QueryProperty(nameof(BookIdString), "BookId")]
    [QueryProperty(nameof(BookTitle), "BookTitle")]
    public partial class BookNotesDetailViewModel : ObservableObject
    {
        private readonly IBookNoteRepository _noteRepository;
        private Guid _currentBookId;

        [ObservableProperty] private string _bookIdString = string.Empty;
        [ObservableProperty] private string _bookTitle = "笔记详情";

        // 列表泛型改为包装器类
        [ObservableProperty] private ObservableCollection<SelectableBookNote> _notesList = new();
        [ObservableProperty] private bool _isBusy;

        // 【新增】：管理模式状态开关
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ManageButtonText))]
        private bool _isManageMode;

        // 动态计算顶部按钮文本
        public string ManageButtonText => IsManageMode ? "取消" : "管理";

        public BookNotesDetailViewModel(IBookNoteRepository noteRepository)
        {
            _noteRepository = noteRepository;
        }

        partial void OnBookIdStringChanged(string value)
        {
            if (Guid.TryParse(value, out Guid bookId))
            {
                _currentBookId = bookId;
                LoadNotesAsync();
            }
        }

        private async void LoadNotesAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                var notes = await _noteRepository.GetNotesByBookIdAsync(_currentBookId);
                // 将实体映射为 UI 包装器
                var wrappedNotes = notes.Select(n => new SelectableBookNote(n)).ToList();
                var newCollection = new ObservableCollection<SelectableBookNote>(wrappedNotes);

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    NotesList = newCollection;
                    IsManageMode = false; // 加载完默认退出管理模式
                });
            }
            catch (Exception ex)
            {
                LocalLogger.LogError($"加载笔记详情失败: {ex.Message}");
            }
            finally
            {
                MainThread.BeginInvokeOnMainThread(() => IsBusy = false);
            }
        }

        // 【新增】：切换管理模式
        [RelayCommand]
        private void ToggleManageMode()
        {
            if (NotesList.Count == 0) return; // 空列表不让点管理
            IsManageMode = !IsManageMode;

            // 如果点击了取消，清空所有勾选状态
            if (!IsManageMode)
            {
                foreach (var item in NotesList) item.IsSelected = false;
            }
        }

        // 【新增】：整行点击切换复选框状态的快捷体验
        [RelayCommand]
        private void ToggleItemSelection(SelectableBookNote item)
        {
            if (IsManageMode && item != null)
            {
                item.IsSelected = !item.IsSelected;
            }
        }

        // 【新增】：执行批量逻辑删除
        [RelayCommand]
        private async Task DeleteSelectedNotesAsync()
        {
            var selectedItems = NotesList.Where(n => n.IsSelected).ToList();
            if (!selectedItems.Any())
            {
                await Application.Current.MainPage!.DisplayAlert("提示", "请至少选择一条要删除的笔记", "确定");
                return;
            }

            bool confirm = await Application.Current.MainPage!.DisplayAlert("确认删除", $"确定要删除这 {selectedItems.Count} 条笔记吗？它们将被移入回收站。", "删除", "取消");
            if (!confirm) return;

            try
            {
                IsBusy = true;
                // 提取 ID 集合
                var idsToDelete = selectedItems.Select(s => s.Note.Id).ToList();

                // 执行数据库物理层逻辑删除
                await _noteRepository.SoftDeleteNotesAsync(idsToDelete);

                // UI 层就地移除，避免重新查库造成的闪烁
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    foreach (var item in selectedItems)
                    {
                        NotesList.Remove(item);
                    }
                    IsManageMode = false;
                });
            }
            catch (Exception ex)
            {
                LocalLogger.LogError($"删除笔记失败: {ex.Message}");
                await Application.Current.MainPage!.DisplayAlert("错误", "删除失败，请稍后重试", "确定");
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task GoBackAsync() => await Shell.Current.GoToAsync("..");
    }
}