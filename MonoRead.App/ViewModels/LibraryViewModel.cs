using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MonoRead.App.Messages;
using MonoRead.Core.Entities;
using MonoRead.Core.Interfaces;
using MonoRead.Infrastructure.Logging;
using System.Collections.ObjectModel;

namespace MonoRead.App.ViewModels
{
    public partial class LibraryViewModel : ObservableObject
    {
        private readonly IFileSystemService _fileSystemService;
        private readonly IBookParsingUseCase _bookParsingUseCase;
        private readonly IBookRepository _bookRepository;
        private readonly IFolderRepository _folderRepository;

        [ObservableProperty]
        private ObservableCollection<LibraryItemNode> _items = new();

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private string _busyMessage = "正在处理...";

        [ObservableProperty]
        private bool _isEditMode;

        [ObservableProperty]
        private string _editModeButtonText = "管理";

        [ObservableProperty]
        private ObservableCollection<LibraryItemNode> _selectedItems = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsNotRoot))]
        [NotifyPropertyChangedFor(nameof(CurrentPathName))]
        private Folder? _currentFolder;

        public bool IsNotRoot => CurrentFolder != null;
        public string CurrentPathName => CurrentFolder == null ? "书架" : CurrentFolder.Name;

        public LibraryViewModel(
            IFileSystemService fileSystemService,
            IBookParsingUseCase bookParsingUseCase,
            IBookRepository bookRepository,
            IFolderRepository folderRepository)
        {
            _fileSystemService = fileSystemService;
            _bookParsingUseCase = bookParsingUseCase;
            _bookRepository = bookRepository;
            _folderRepository = folderRepository;

            LoadItemsCommand.Execute(null);

            WeakReferenceMessenger.Default.Register<FileImportMessage>(this, async (r, m) =>
            {
                if (App.PendingImportFilePath == m.SandboxPath)
                {
                    App.PendingImportFilePath = null;
                    App.PendingImportFileName = null;
                }
                await ProcessImportedFileAsync(m.SandboxPath, m.FileName);
            });
        }

        [RelayCommand]
        private async Task LoadItemsAsync()
        {
            try
            {
                var nodes = new List<LibraryItemNode>();

                if (CurrentFolder == null)
                {
                    var folders = await _folderRepository.GetAllAsync();
                    foreach (var f in folders.OrderBy(x => x.SortOrder))
                    {
                        nodes.Add(new LibraryItemNode
                        {
                            IsFolder = true,
                            Id = f.Id,
                            Title = f.Name,
                            Subtitle = "文件夹",
                            OriginalEntity = f,
                            ShowCheckBox = IsEditMode // 【修复】：继承当前编辑状态
                        });
                    }
                }

                var allBooks = await _bookRepository.GetAllBooksAsync();
                var currentLevelBooks = allBooks
                    .Where(b => !b.IsDeleted && b.FolderId == CurrentFolder?.Id)
                    .ToList();

                foreach (var b in currentLevelBooks)
                {
                    nodes.Add(new LibraryItemNode
                    {
                        IsFolder = false,
                        Id = b.Id,
                        Title = b.Title,
                        Subtitle = b.ProgressText,
                        OriginalEntity = b,
                        ShowCheckBox = IsEditMode
                    });
                }

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    Items.Clear();
                    foreach (var node in nodes) Items.Add(node);
                });
            }
            catch (Exception ex)
            {
                LocalLogger.LogError($"加载混合书架异常: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task GoBackAsync()
        {
            CurrentFolder = null;
            await LoadItemsAsync();
        }

        [RelayCommand]
        private async Task OpenItemAsync(LibraryItemNode node)
        {
            if (node == null) return;

            if (IsEditMode)
            {
                // 【放权】：允许文件夹在编辑模式下被选中（为了后续的批量删除）
                node.IsSelected = !node.IsSelected;
                if (node.IsSelected && !SelectedItems.Contains(node)) SelectedItems.Add(node);
                else if (!node.IsSelected && SelectedItems.Contains(node)) SelectedItems.Remove(node);
                return;
            }

            if (IsBusy) return;
            IsBusy = true;
            try
            {
                await Task.Delay(50);
                if (node.IsFolder && node.OriginalEntity is Folder folder)
                {
                    CurrentFolder = folder;
                    await LoadItemsAsync();
                }
                else if (!node.IsFolder && node.OriginalEntity is Book book)
                {
                    string route = $"{nameof(Views.ReaderPage)}?BookId={book.Id}";
                    await Shell.Current.GoToAsync(route);
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task ImportBookAsync()
        {
            // (导入逻辑保持原样)
            if (IsBusy) return;
            try
            {
                var result = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "请选择TXT小说文件" });
                if (result != null)
                {
                    IsBusy = true;
                    BusyMessage = "极速拆解中...";
                    await Task.Delay(50);

                    var parsedBook = await Task.Run(async () =>
                    {
                        using var stream = await result.OpenReadAsync();
                        string newFileName = $"{Guid.NewGuid()}.dat";
                        string sandboxPath = await _fileSystemService.CopyFileToSandboxAsync(stream, newFileName);
                        string fileHash = await _fileSystemService.CalculateFileHashAsync(sandboxPath);
                        return await _bookParsingUseCase.ParseAndSplitBookAsync(sandboxPath, result.FileName, fileHash);
                    });

                    if (CurrentFolder != null)
                    {
                        parsedBook.FolderId = CurrentFolder.Id;
                        await _bookRepository.UpdateAsync(parsedBook);
                    }

                    MainThread.BeginInvokeOnMainThread(async () =>
                    {
                        LoadItemsCommand.Execute(null);
                        await OpenItemAsync(new LibraryItemNode { IsFolder = false, OriginalEntity = parsedBook, ShowCheckBox = IsEditMode });
                    });
                }
            }
            catch (Exception ex)
            {
                MainThread.BeginInvokeOnMainThread(() => Application.Current.MainPage.DisplayAlert("导入失败", ex.Message, "确定"));
            }
            finally { IsBusy = false; }
        }

        [RelayCommand]
        private async Task CheckPendingImportAsync()
        {
            if (!string.IsNullOrEmpty(App.PendingImportFilePath))
            {
                string path = App.PendingImportFilePath;
                string name = App.PendingImportFileName ?? "未知文档";
                App.PendingImportFilePath = null;
                App.PendingImportFileName = null;
                await ProcessImportedFileAsync(path, name);
            }
        }

        private async Task ProcessImportedFileAsync(string sandboxPath, string fileName)
        {
            // (原导入防重逻辑保持不变)
            if (IsBusy) return;
            IsBusy = true;
            BusyMessage = "正在极速拆解小说章节，请稍候...";
            await Task.Delay(50);

            try
            {
                string fileHash = await _fileSystemService.CalculateFileHashAsync(sandboxPath);
                var allBooks = await _bookRepository.GetAllBooksAsync();
                var existingBook = allBooks.FirstOrDefault(b => b.FileHash == fileHash);

                if (existingBook != null)
                {
                    if (File.Exists(sandboxPath)) File.Delete(sandboxPath);
                    MainThread.BeginInvokeOnMainThread(async () =>
                    {
                        await OpenItemAsync(new LibraryItemNode { IsFolder = false, OriginalEntity = existingBook, ShowCheckBox = IsEditMode });
                    });
                    return;
                }

                var parsedBook = await Task.Run(async () =>
                {
                    return await _bookParsingUseCase.ParseAndSplitBookAsync(sandboxPath, fileName, fileHash);
                });

                if (CurrentFolder != null)
                {
                    parsedBook.FolderId = CurrentFolder.Id;
                    await _bookRepository.UpdateAsync(parsedBook);
                }

                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    LoadItemsCommand.Execute(null);
                    await OpenItemAsync(new LibraryItemNode { IsFolder = false, OriginalEntity = parsedBook, ShowCheckBox = IsEditMode });
                });
            }
            catch (Exception ex)
            {
                if (File.Exists(sandboxPath)) File.Delete(sandboxPath);
                LocalLogger.LogError($"处理外部导入异常: {fileName}", ex);
                MainThread.BeginInvokeOnMainThread(() => Application.Current.MainPage.DisplayAlert("导入失败", ex.Message, "确定"));
            }
            finally { IsBusy = false; }
        }

        //[RelayCommand]
        //private async Task GoToTrashAsync()
        //{
        //    try { await Shell.Current.GoToAsync("TrashPage"); }
        //    catch (Exception) { await Application.Current.MainPage.DisplayAlert("提示", "回收站页面暂未创建", "确定"); }
        //}

        //[RelayCommand]
        //private async Task ExportLogAsync() { /* 原有导出逻辑 */ }

        // 【废弃指令】：ItemLongPressCommand 已根据要求被抹除。

        [RelayCommand]
        private void ToggleEditMode()
        {
            IsEditMode = !IsEditMode;
            EditModeButtonText = IsEditMode ? "完成" : "管理";

            foreach (var item in Items)
            {
                if (!IsEditMode) item.IsSelected = false;
                // 【放权】：现在无论书还是文件夹，只要进入编辑模式，就展示复选框
                item.ShowCheckBox = IsEditMode;
            }

            if (!IsEditMode) SelectedItems.Clear();
        }

        // 【新增：批量摧毁引擎】
        [RelayCommand]
        private async Task DeleteSelectedItemsAsync()
        {
            if (!SelectedItems.Any()) return;

            bool confirm = await Application.Current.MainPage.DisplayAlert("批量删除",
                $"确定要将选中的 {SelectedItems.Count} 项移入回收站吗？\n(删除文件夹将同时移出其内部的所有书籍)", "确定删除", "取消");

            if (!confirm) return;

            IsBusy = true;
            BusyMessage = "正在清理数据...";

            try
            {
                foreach (var node in SelectedItems.ToList())
                {
                    if (node.IsFolder && node.OriginalEntity is Folder folder)
                    {
                        // 1. 级联软删除文件夹内的书籍
                        var folderBooks = (await _bookRepository.GetAllBooksAsync()).Where(b => b.FolderId == folder.Id).ToList();
                        foreach (var b in folderBooks)
                        {
                            b.IsDeleted = true;
                            await _bookRepository.UpdateAsync(b);
                        }
                        // 2. 物理抹除当前层级的文件夹实体
                        await _folderRepository.DeleteAsync(folder);
                    }
                    else if (!node.IsFolder && node.OriginalEntity is Book book)
                    {
                        book.IsDeleted = true;
                        await _bookRepository.UpdateAsync(book);
                    }
                }

                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    ToggleEditModeCommand.Execute(null);
                    await LoadItemsAsync();
                });
            }
            catch (Exception ex)
            {
                LocalLogger.LogError("批量删除异常", ex);
                MainThread.BeginInvokeOnMainThread(() => Application.Current.MainPage.DisplayAlert("删除失败", "底层数据清理异常，请重试。", "确定"));
            }
            finally { IsBusy = false; }
        }

        [RelayCommand]
        private async Task MoveSelectedItemsToFolderAsync()
        {
            if (!SelectedItems.Any()) return;

            // 【防呆拦截】：检查是否选了文件夹
            var selectedFolders = SelectedItems.Where(i => i.IsFolder).ToList();
            if (selectedFolders.Any())
            {
                await Application.Current.MainPage.DisplayAlert("操作冲突", "文件夹不支持嵌套移动，请取消勾选文件夹后再试。", "知道了");
                return;
            }

            var booksToMove = SelectedItems.Where(i => !i.IsFolder).ToList();

            try
            {
                var allFolders = await _folderRepository.GetAllAsync();
                var folderNames = allFolders.Select(f => f.Name).ToList();
                folderNames.Insert(0, "根书架");
                folderNames.Add("新建文件夹...");

                string action = await Application.Current.MainPage.DisplayActionSheet("移动到", "取消", null, folderNames.ToArray());
                if (action == "取消" || string.IsNullOrEmpty(action)) return;

                Guid? targetFolderId = null;

                if (action == "新建文件夹...")
                {
                    string newName = await Application.Current.MainPage.DisplayPromptAsync("新建文件夹", "请输入名称", "确定", "取消");
                    if (string.IsNullOrWhiteSpace(newName)) return;

                    await Task.Delay(50);

                    var newFolder = new Folder { Id = Guid.NewGuid(), Name = newName, SortOrder = 0 };
                    await _folderRepository.AddAsync(newFolder);
                    targetFolderId = newFolder.Id;
                }
                else if (action != "根书架")
                {
                    targetFolderId = allFolders.First(f => f.Name == action).Id;
                }

                foreach (var node in booksToMove)
                {
                    if (node.OriginalEntity is Book b)
                    {
                        b.FolderId = targetFolderId;
                        await _bookRepository.UpdateAsync(b);
                    }
                }

                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await Application.Current.MainPage.DisplayAlert("成功", $"已将 {booksToMove.Count} 本书移至【{action}】", "确定");
                    ToggleEditModeCommand.Execute(null);
                    await LoadItemsAsync();
                });
            }
            catch (Exception ex)
            {
                string realError = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                LocalLogger.LogError($"移动文件夹致命异常: {realError}", ex);

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    Application.Current.MainPage.DisplayAlert("操作失败", $"底层发生错误。\n错误详情: {realError}", "知道了");
                });
            }
        }
    }
}