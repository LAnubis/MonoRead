using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MonoRead.App.Messages;
using MonoRead.Core.Entities;
using MonoRead.Core.Interfaces;
using MonoRead.Infrastructure.Logging;
using System.Collections.ObjectModel;
using System.Linq;

namespace MonoRead.App.ViewModels
{
    public partial class LibraryViewModel : ObservableObject
    {
        private readonly IFileSystemService _fileSystemService;
        private readonly IBookParsingUseCase _bookParsingUseCase;
        private readonly IBookRepository _bookRepository;
        private readonly IFolderRepository _folderRepository;

        // 【修改点 1】：新增云端服务的依赖注入字段
        private readonly ICloudStorageService _cloudStorageService;

        [ObservableProperty] private ObservableCollection<LibraryItemNode> _items = new();
        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private string _busyMessage = "正在处理...";
        [ObservableProperty] private bool _isEditMode;
        [ObservableProperty] private string _editModeButtonText = "管理";
        [ObservableProperty] private ObservableCollection<LibraryItemNode> _selectedItems = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsNotRoot))]
        [NotifyPropertyChangedFor(nameof(CurrentPathName))]
        private Folder? _currentFolder;

        public bool IsNotRoot => CurrentFolder != null;
        public string CurrentPathName => CurrentFolder == null ? "书架" : CurrentFolder.Name;

        // 【修改点 2】：在构造函数签名中加入 ICloudStorageService
        public LibraryViewModel(
            IFileSystemService fileSystemService,
            IBookParsingUseCase bookParsingUseCase,
            IBookRepository bookRepository,
            IFolderRepository folderRepository,
            ICloudStorageService cloudStorageService)
        {
            _fileSystemService = fileSystemService;
            _bookParsingUseCase = bookParsingUseCase;
            _bookRepository = bookRepository;
            _folderRepository = folderRepository;

            // 【修改点 3】：完成服务赋值
            _cloudStorageService = cloudStorageService;

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

            WeakReferenceMessenger.Default.Register<CloudFileSelectedMessage>(this, async (r, m) =>
            {
                await ProcessCloudImportAsync(m.RemoteFilePath, m.DisplayName);
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
                            ShowCheckBox = IsEditMode
                        });
                    }
                }

                var allBooks = await _bookRepository.GetAllBooksAsync();
                var currentLevelBooks = allBooks
                    .Where(b => !b.IsDeleted && b.FolderId == CurrentFolder?.Id)
                    .ToList();

                foreach (var b in currentLevelBooks)
                {
                    var fullBook = await _bookRepository.GetBookWithChaptersAsync(b.Id) ?? b;

                    nodes.Add(new LibraryItemNode
                    {
                        IsFolder = false,
                        Id = fullBook.Id,
                        Title = fullBook.Title,
                        Subtitle = fullBook.ProgressText,
                        OriginalEntity = fullBook,
                        ShowCheckBox = IsEditMode
                    });
                }

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    Items.Clear();
                    foreach (var node in nodes) Items.Add(node);
                });
            }
            catch (Exception ex) { LocalLogger.LogError($"加载混合书架异常: {ex.Message}"); }
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
            finally { IsBusy = false; }
        }

        [RelayCommand]
        private async Task ImportBookAsync()
        {
            if (IsBusy) return;

            string action = await Application.Current.MainPage!.DisplayActionSheet(
                "选择导入方式", "取消", null, "本地导入 (TXT)", "坚果云导入 (TXT)");

            if (action == "本地导入 (TXT)")
            {
                await ExecuteLocalImportAsync();
            }
            else if (action == "坚果云导入 (TXT)")
            {
                await ExecuteCloudImportStarterAsync();
            }
        }

        private async Task ExecuteLocalImportAsync()
        {
            try
            {
                var result = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "请选择TXT小说文件" });
                if (result != null)
                {
                    if (!result.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    {
                        await Application.Current.MainPage!.DisplayAlert("格式错误", "MonoRead 目前仅支持导入 .txt 格式的纯文本小说。", "知道了");
                        return;
                    }

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
                MainThread.BeginInvokeOnMainThread(() => Application.Current.MainPage!.DisplayAlert("导入失败", ex.Message, "确定"));
            }
            finally { IsBusy = false; }
        }

        private async Task ExecuteCloudImportStarterAsync()
        {
            var savedUrl = await SecureStorage.Default.GetAsync("WebDav_Url");
            if (string.IsNullOrEmpty(savedUrl))
            {
                bool goConfig = await Application.Current.MainPage!.DisplayAlert("未配置", "您尚未配置坚果云账号，是否立即前往设置？", "前往配置", "取消");
                if (goConfig) await Shell.Current.GoToAsync("CloudBackupPage");
                return;
            }

            await Shell.Current.GoToAsync("CloudFilePickerPage");
        }

        private async Task ProcessCloudImportAsync(string remoteFilePath, string displayName)
        {
            if (IsBusy) return;
            IsBusy = true;
            BusyMessage = "正在从坚果云极速下载...";

            try
            {
                var url = await SecureStorage.Default.GetAsync("WebDav_Url") ?? "";
                var user = await SecureStorage.Default.GetAsync("WebDav_User") ?? "";
                var pass = await SecureStorage.Default.GetAsync("WebDav_Pass") ?? "";

                string newFileName = $"{Guid.NewGuid()}.dat";
                string sandboxPath = Path.Combine(FileSystem.AppDataDirectory, "Library", newFileName);
                Directory.CreateDirectory(Path.GetDirectoryName(sandboxPath)!);

                // 【修改点 4】：使用构造函数注入的服务调用下载，彻底消灭 Handler 报错
                bool downloadSuccess = await _cloudStorageService.DownloadFileAsync(url, user, pass, remoteFilePath, sandboxPath);
                if (!downloadSuccess) throw new Exception("网络下载中断或失败。");

                BusyMessage = "极速拆解小说章节中...";
                await Task.Delay(50);

                string fileHash = await _fileSystemService.CalculateFileHashAsync(sandboxPath);
                var allBooks = await _bookRepository.GetAllBooksAsync();
                var existingBook = allBooks.FirstOrDefault(b => b.FileHash == fileHash);

                if (existingBook != null)
                {
                    File.Delete(sandboxPath);
                    await Application.Current.MainPage!.DisplayAlert("提示", "该书籍已在书架中，无需重复导入。", "知道了");
                    return;
                }

                var parsedBook = await Task.Run(async () =>
                {
                    return await _bookParsingUseCase.ParseAndSplitBookAsync(sandboxPath, displayName, fileHash);
                });

                if (CurrentFolder != null)
                {
                    parsedBook.FolderId = CurrentFolder.Id;
                    await _bookRepository.UpdateAsync(parsedBook);
                }

                LoadItemsCommand.Execute(null);
                await OpenItemAsync(new LibraryItemNode { IsFolder = false, OriginalEntity = parsedBook, ShowCheckBox = IsEditMode });
            }
            catch (Exception ex)
            {
                LocalLogger.LogError($"云端导入异常: {ex.Message}");
                await Application.Current.MainPage!.DisplayAlert("导入失败", $"云端下载或解析失败：{ex.Message}", "确定");
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
                MainThread.BeginInvokeOnMainThread(() => Application.Current.MainPage!.DisplayAlert("导入失败", ex.Message, "确定"));
            }
            finally { IsBusy = false; }
        }

        [RelayCommand]
        private void ToggleEditMode()
        {
            IsEditMode = !IsEditMode;
            EditModeButtonText = IsEditMode ? "完成" : "管理";

            foreach (var item in Items)
            {
                if (!IsEditMode) item.IsSelected = false;
                item.ShowCheckBox = IsEditMode;
            }

            if (!IsEditMode) SelectedItems.Clear();
        }

        [RelayCommand]
        private async Task DeleteSelectedItemsAsync()
        {
            if (!SelectedItems.Any()) return;

            bool confirm = await Application.Current.MainPage!.DisplayAlert("批量删除",
                $"确定要将选中的 {SelectedItems.Count} 项移入回收站吗？\n(删除文件夹将同时移出其内部的所有书籍)", "确定删除", "取消");

            if (!confirm) return;

            IsBusy = true;
            BusyMessage = "正在安全清理数据...";

            try
            {
                var booksToDelete = new List<Book>();
                var foldersToDelete = new List<Folder>();
                var allBooks = await _bookRepository.GetAllBooksAsync();

                foreach (var node in SelectedItems.ToList())
                {
                    if (node.IsFolder && node.OriginalEntity is Folder folder)
                    {
                        foldersToDelete.Add(folder);
                        booksToDelete.AddRange(allBooks.Where(b => b.FolderId == folder.Id));
                    }
                    else if (!node.IsFolder && node.OriginalEntity is Book book)
                    {
                        booksToDelete.Add(book);
                    }
                }

                booksToDelete = booksToDelete.GroupBy(b => b.Id).Select(g => g.First()).ToList();

                bool hasAnyNotes = false;
                foreach (var book in booksToDelete)
                {
                    if (await _bookRepository.HasActiveNotesAsync(book.Id))
                    {
                        hasAnyNotes = true;
                        break;
                    }
                }

                bool archiveNotes = false;
                if (hasAnyNotes)
                {
                    IsBusy = false;
                    string actionStr = await Application.Current.MainPage.DisplayActionSheet(
                        "检测到您选中的书籍中包含【读书笔记】，请选择处理方式：",
                        "取消删除", null,
                        "仅移出书籍（笔记保留为未分类）",
                        "书籍和笔记一起移入回收站");

                    if (actionStr == "仅移出书籍（笔记保留为未分类）") { archiveNotes = false; }
                    else if (actionStr == "书籍和笔记一起移入回收站") { archiveNotes = true; }
                    else { return; }

                    IsBusy = true;
                }

                foreach (var book in booksToDelete)
                {
                    await _bookRepository.ArchiveBookSafelyAsync(book.Id, archiveNotes);
                }

                foreach (var folder in foldersToDelete)
                {
                    await _folderRepository.DeleteAsync(folder);
                }

                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    ToggleEditModeCommand.Execute(null);
                    await LoadItemsAsync();
                    await Application.Current.MainPage.DisplayAlert("清理完成", "所选项已安全移入回收站", "确定");
                });
            }
            catch (Exception ex)
            {
                LocalLogger.LogError("批量删除异常", ex);
                MainThread.BeginInvokeOnMainThread(() => Application.Current.MainPage!.DisplayAlert("删除失败", "底层数据清理异常，请重试。", "确定"));
            }
            finally { IsBusy = false; }
        }

        [RelayCommand]
        private async Task MoveSelectedItemsToFolderAsync()
        {
            if (!SelectedItems.Any()) return;

            var selectedFolders = SelectedItems.Where(i => i.IsFolder).ToList();
            if (selectedFolders.Any())
            {
                await Application.Current.MainPage!.DisplayAlert("操作冲突", "文件夹不支持嵌套移动，请取消勾选文件夹后再试。", "知道了");
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
                    Application.Current.MainPage!.DisplayAlert("操作失败", $"底层发生错误。\n错误详情: {realError}", "知道了");
                });
            }
        }
    }
}