using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MonoRead.App.Messages;
using MonoRead.Core.Entities;
using MonoRead.Core.Interfaces;
using MonoRead.Infrastructure.Logging;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using System.Threading.Tasks;

namespace MonoRead.App.ViewModels
{
    public partial class LibraryViewModel : ObservableObject
    {
        private readonly IFileSystemService _fileSystemService;
        private readonly IBookParsingUseCase _bookParsingUseCase;
        private readonly IBookRepository _bookRepository;
        private readonly IFolderRepository _folderRepository;
        private readonly ICloudStorageService _cloudStorageService;

        private static bool _isMessengerProcessing = false;

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
            _cloudStorageService = cloudStorageService;

            LoadItemsCommand.Execute(null);

            WeakReferenceMessenger.Default.Register<FileImportMessage>(this, async (r, m) =>
            {
                if (_isMessengerProcessing) return;
                _isMessengerProcessing = true;
                try
                {
                    if (App.PendingImportFilePath == m.SandboxPath)
                    {
                        App.PendingImportFilePath = null;
                        App.PendingImportFileName = null;
                    }
                    await ((LibraryViewModel)r).ProcessImportedFileAsync(m.SandboxPath, m.FileName);
                }
                finally { _isMessengerProcessing = false; }
            });

            WeakReferenceMessenger.Default.Register<CloudFilesSelectedMessage>(this, async (r, m) =>
            {
                if (_isMessengerProcessing) return;
                _isMessengerProcessing = true;
                try
                {
                    await ((LibraryViewModel)r).ProcessCloudImportBatchAsync(m.SelectedFiles);
                }
                finally { _isMessengerProcessing = false; }
            });
        }

        public static readonly System.Threading.SemaphoreSlim GlobalDbLock = new(1, 1);

        private async Task RefreshItemsCoreAsync()
        {
            var nodes = await Task.Run(async () =>
            {
                await GlobalDbLock.WaitAsync();
                try
                {
                    var tempList = new List<LibraryItemNode>();

                    if (CurrentFolder == null)
                    {
                        var folders = await _folderRepository.GetAllAsync();
                        foreach (var f in folders.OrderBy(x => x.SortOrder))
                        {
                            tempList.Add(new LibraryItemNode { IsFolder = true, Id = f.Id, Title = f.Name, Subtitle = "文件夹", OriginalEntity = f, ShowCheckBox = IsEditMode });
                        }
                    }

                    var allBooks = await _bookRepository.GetAllBooksAsync();
                    var currentLevelBooks = allBooks.Where(b => !b.IsDeleted && b.FolderId == CurrentFolder?.Id).ToList();

                    foreach (var b in currentLevelBooks)
                    {
                        tempList.Add(new LibraryItemNode
                        {
                            IsFolder = false,
                            Id = b.Id,
                            Title = b.Title,
                            Subtitle = b.ProgressText,
                            OriginalEntity = b,
                            ShowCheckBox = IsEditMode
                        });
                    }
                    return tempList;
                }
                finally
                {
                    GlobalDbLock.Release();
                }
            });

            MainThread.BeginInvokeOnMainThread(() =>
            {
                Items.Clear();
                foreach (var node in nodes)
                {
                    Items.Add(node);
                }
            });
        }
        [RelayCommand]
        private async Task LoadItemsAsync()
        {
            if (IsBusy) return;
            IsBusy = true;

            // 【核心修复】：确保加载书架时显示正确的文字
            BusyMessage = "正在同步您的书架...";

            try
            {
                await RefreshItemsCoreAsync();
            }
            catch (Exception ex)
            {
                LocalLogger.LogError($"加载书架异常: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        // =========================================================
        // 【全新体验】：静默无感刷新，不触发全屏遮罩
        // =========================================================
        [RelayCommand]
        private async Task SilentRefreshAsync()
        {
            try
            {
                // 偷偷在后台比对并刷新数据，UI 不会出现闪烁
                await RefreshItemsCoreAsync();
            }
            catch (Exception ex)
            {
                LocalLogger.LogError($"静默加载异常: {ex.Message}");
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

            // 编辑模式逻辑保持不变...

            if (IsBusy) return;
            IsBusy = true;

            // 【核心修复】：点击进入书籍时，明确设置提示词
            BusyMessage = "正在为您开启阅读体验...";

            try
            {
                await Task.Delay(50); // 给 UI 渲染一点时间
                if (node.IsFolder && node.OriginalEntity is Folder folder)
                {
                    CurrentFolder = folder;
                    await RefreshItemsCoreAsync();
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
            if (IsBusy) return;

            // 彻底去除了“全网搜书”选项
            string action = await Shell.Current.DisplayActionSheetAsync(
                "导入本地或云端书籍", "取消", null, "本地导入 (TXT)", "坚果云导入 (TXT)");

            if (action == "本地导入 (TXT)") await ExecuteLocalImportAsync();
            else if (action == "坚果云导入 (TXT)") await ExecuteCloudImportStarterAsync();
        }

        private async Task ExecuteLocalImportAsync()
        {
            try
            {
                var results = await FilePicker.Default.PickMultipleAsync(new PickOptions { PickerTitle = "请选择TXT小说 (最多10本)" });
                if (results == null || !results.Any()) return;

                var txtFiles = results.Where(r => r.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)).ToList();

                if (!txtFiles.Any())
                {
                    await Shell.Current.DisplayAlert("提示", "未检测到有效的 .txt 文件。", "知道了");
                    return;
                }

                if (txtFiles.Count > 10)
                {
                    await Shell.Current.DisplayAlert("超出限制", $"为了保证手机流畅运行，每次最多允许导入 10 本书籍。\n您当前选择了 {txtFiles.Count} 本，请重新选择。", "知道了");
                    return;
                }

                IsBusy = true;
                int totalCount = txtFiles.Count;
                int currentIndex = 0;
                int successCount = 0;

                foreach (var file in txtFiles)
                {
                    currentIndex++;
                    BusyMessage = $"极速拆解中 ({currentIndex}/{totalCount})...";
                    await Task.Delay(50);

                    try
                    {
                        var parsedBook = await Task.Run(async () =>
                        {
                            using var stream = await file.OpenReadAsync();
                            string newFileName = $"{Guid.NewGuid()}.dat";
                            string sandboxPath = await _fileSystemService.CopyFileToSandboxAsync(stream, newFileName);
                            string fileHash = await _fileSystemService.CalculateFileHashAsync(sandboxPath);
                            return await _bookParsingUseCase.ParseAndSplitBookAsync(sandboxPath, file.FileName, fileHash);
                        });

                        if (CurrentFolder != null)
                        {
                            parsedBook.FolderId = CurrentFolder.Id;
                            await _bookRepository.UpdateAsync(parsedBook);
                        }
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        LocalLogger.LogError($"导入单本书籍失败: {file.FileName}", ex);
                    }
                }

                await RefreshItemsCoreAsync();

                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    if (totalCount == 1 && successCount == 1)
                        await Shell.Current.DisplayAlert("导入成功", "书籍已成功加入书架", "开始阅读");
                    else
                        await Shell.Current.DisplayAlert("批量处理完成", $"共选择 {totalCount} 本，成功导入 {successCount} 本。", "确定");
                });
            }
            catch (Exception ex)
            {
                MainThread.BeginInvokeOnMainThread(() => Shell.Current.DisplayAlert("导入失败", ex.Message, "确定"));
            }
            finally { IsBusy = false; }
        }

        private async Task ExecuteCloudImportStarterAsync()
        {
            var savedUrl = await SecureStorage.Default.GetAsync("WebDav_Url");
            if (string.IsNullOrEmpty(savedUrl))
            {
                bool goConfig = await Shell.Current.DisplayAlert("未配置", "您尚未配置坚果云账号，是否立即前往设置？", "前往配置", "取消");
                if (goConfig) await Shell.Current.GoToAsync("CloudBackupPage");
                return;
            }
            await Shell.Current.GoToAsync("CloudFilePickerPage");
        }

        private async Task ProcessCloudImportBatchAsync(List<(string RemoteFilePath, string DisplayName)> files)
        {
            if (files == null || !files.Any()) return;

            if (files.Count > 10)
            {
                await Shell.Current.DisplayAlert("超出限制", $"云端下载极耗内存，每次最多允许导入 10 本书籍。\n您当前选择了 {files.Count} 本，请重新选择。", "知道了");
                return;
            }

            if (IsBusy) return;
            IsBusy = true;

            try
            {
                var url = await SecureStorage.Default.GetAsync("WebDav_Url") ?? "";
                var user = await SecureStorage.Default.GetAsync("WebDav_User") ?? "";
                var pass = await SecureStorage.Default.GetAsync("WebDav_Pass") ?? "";

                int totalCount = files.Count;
                int currentIndex = 0;
                int successCount = 0;

                foreach (var file in files)
                {
                    currentIndex++;
                    BusyMessage = $"正在下载 ({currentIndex}/{totalCount}):\n{file.DisplayName}";
                    await Task.Delay(50);

                    try
                    {
                        string tempFileName = $"{Guid.NewGuid()}.dat";
                        string tempFilePath = Path.Combine(FileSystem.CacheDirectory, tempFileName);

                        bool downloadSuccess = await _cloudStorageService.DownloadFileAsync(url, user, pass, file.RemoteFilePath, tempFilePath);
                        if (!downloadSuccess) throw new Exception("下载失败");

                        BusyMessage = $"极速拆解中 ({currentIndex}/{totalCount})...";
                        await Task.Delay(50);

                        string sandboxPath;
                        using (var tempStream = File.OpenRead(tempFilePath))
                        {
                            sandboxPath = await _fileSystemService.CopyFileToSandboxAsync(tempStream, $"{Guid.NewGuid()}.dat");
                        }
                        if (File.Exists(tempFilePath)) File.Delete(tempFilePath);

                        string fileHash = await _fileSystemService.CalculateFileHashAsync(sandboxPath);
                        var allBooks = await _bookRepository.GetAllBooksAsync();
                        var existingBook = allBooks.FirstOrDefault(b => b.FileHash == fileHash);

                        if (existingBook != null)
                        {
                            if (File.Exists(sandboxPath)) File.Delete(sandboxPath);
                            successCount++;
                            continue;
                        }

                        var parsedBook = await Task.Run(async () =>
                        {
                            return await _bookParsingUseCase.ParseAndSplitBookAsync(sandboxPath, file.DisplayName, fileHash);
                        });

                        if (CurrentFolder != null)
                        {
                            parsedBook.FolderId = CurrentFolder.Id;
                            await _bookRepository.UpdateAsync(parsedBook);
                        }
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        LocalLogger.LogError($"云端处理失败 [{file.DisplayName}]: {ex.Message}");
                    }
                }

                await RefreshItemsCoreAsync();

                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    if (totalCount == 1 && successCount == 1)
                        await Shell.Current.DisplayAlert("云端导入成功", "书籍已就绪", "确定");
                    else
                        await Shell.Current.DisplayAlert("云端批量处理完成", $"共下载 {totalCount} 本，成功导入 {successCount} 本。", "确定");
                });
            }
            catch (Exception ex)
            {
                MainThread.BeginInvokeOnMainThread(() => Shell.Current.DisplayAlert("严重异常", $"云端批量管线崩溃：{ex.Message}", "确定"));
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
                    await RefreshItemsCoreAsync();
                    MainThread.BeginInvokeOnMainThread(async () =>
                    {
                        var targetNode = Items.FirstOrDefault(i => i.OriginalEntity is Book b && b.Id == existingBook.Id);
                        if (targetNode != null) await OpenItemAsync(targetNode);
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

                await RefreshItemsCoreAsync();

                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    var targetNode = Items.FirstOrDefault(i => i.OriginalEntity is Book b && b.Id == parsedBook.Id);
                    if (targetNode != null) await OpenItemAsync(targetNode);
                });
            }
            catch (Exception ex)
            {
                if (File.Exists(sandboxPath)) File.Delete(sandboxPath);
                LocalLogger.LogError($"处理外部导入异常: {fileName}", ex);
                MainThread.BeginInvokeOnMainThread(() => Shell.Current.DisplayAlert("导入失败", ex.Message, "确定"));
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

            bool confirm = await Shell.Current.DisplayAlert("批量删除",
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
                    string actionStr = await Shell.Current.DisplayActionSheetAsync(
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
                    try { book.IsDeleted = true; await _bookRepository.UpdateAsync(book); } catch { }
                    await _bookRepository.ArchiveBookSafelyAsync(book.Id, archiveNotes);
                }

                foreach (var folder in foldersToDelete)
                {
                    await _folderRepository.DeleteAsync(folder);
                }

                await RefreshItemsCoreAsync();

                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    ToggleEditModeCommand.Execute(null);
                    await Shell.Current.DisplayAlert("清理完成", "所选项已安全移除", "确定");
                });
            }
            catch (Exception ex)
            {
                LocalLogger.LogError("批量删除异常", ex);
                MainThread.BeginInvokeOnMainThread(() => Shell.Current.DisplayAlert("删除失败", "底层数据清理异常，请重试。", "确定"));
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
                await Shell.Current.DisplayAlert("操作冲突", "文件夹不支持嵌套移动，请取消勾选文件夹后再试。", "知道了");
                return;
            }

            var booksToMove = SelectedItems.Where(i => !i.IsFolder).ToList();

            try
            {
                var allFolders = await _folderRepository.GetAllAsync();
                var folderNames = allFolders.Select(f => f.Name).ToList();
                folderNames.Insert(0, "根书架");
                folderNames.Add("新建文件夹...");

                string action = await Shell.Current.DisplayActionSheetAsync("移动到", "取消", null, folderNames.ToArray());
                if (action == "取消" || string.IsNullOrEmpty(action)) return;

                Guid? targetFolderId = null;

                if (action == "新建文件夹...")
                {
                    string newName = await Shell.Current.DisplayPromptAsync("新建文件夹", "请输入名称", "确定", "取消");
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

                await RefreshItemsCoreAsync();

                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await Shell.Current.DisplayAlert("成功", $"已将 {booksToMove.Count} 本书移至【{action}】", "确定");
                    ToggleEditModeCommand.Execute(null);
                });
            }
            catch (Exception ex)
            {
                string realError = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                LocalLogger.LogError($"移动文件夹致命异常: {realError}", ex);

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    Shell.Current.DisplayAlert("操作失败", $"底层发生错误。\n错误详情: {realError}", "知道了");
                });
            }
        }
    }
}