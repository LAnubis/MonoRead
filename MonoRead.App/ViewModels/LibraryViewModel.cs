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
using System.Text;
using System.Text.Json;
using Microsoft.Maui.Graphics;

namespace MonoRead.App.ViewModels
{
    public partial class LibraryViewModel : ObservableObject
    {
        private readonly IFileSystemService _fileSystemService;
        private readonly IBookParsingUseCase _bookParsingUseCase;
        private readonly IBookRepository _bookRepository;
        private readonly IFolderRepository _folderRepository;
        private readonly ICloudStorageService _cloudStorageService;
        private readonly IBookSearchEngine _searchEngine;

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
            ICloudStorageService cloudStorageService,
            IBookSearchEngine searchEngine)
        {
            _fileSystemService = fileSystemService;
            _bookParsingUseCase = bookParsingUseCase;
            _bookRepository = bookRepository;
            _folderRepository = folderRepository;
            _cloudStorageService = cloudStorageService;
            _searchEngine = searchEngine;

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
                        bool isCloud = b.FilePath == "ONLINE_GHOST";

                        tempList.Add(new LibraryItemNode
                        {
                            IsFolder = false,
                            Id = b.Id,
                            Title = b.Title,
                            Subtitle = b.ProgressText,
                            OriginalEntity = b,
                            ShowCheckBox = IsEditMode,
                            IsCloudBook = isCloud,
                            StatusBadgeText = isCloud ? "☁️ 云端" : "📱 本地",
                            BadgeBackgroundColor = isCloud ? Color.FromArgb("#E1F5FE") : Color.FromArgb("#F5F5F5"),
                            BadgeTextColor = isCloud ? Color.FromArgb("#0288D1") : Color.FromArgb("#9E9E9E")
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
                    await RefreshItemsCoreAsync();
                }
                else if (!node.IsFolder && node.OriginalEntity is Book book)
                {
                    if (node.IsCloudBook)
                    {
                        bool confirm = await Shell.Current.DisplayAlert("开始下载", $"《{book.Title}》是一本云端书籍。\n是否立即全本下载并转为本地保存？", "立即下载", "取消");
                        if (confirm)
                        {
                            await DownloadCloudBookToLocalAsync(book);
                        }
                        return;
                    }

                    string route = $"{nameof(Views.ReaderPage)}?BookId={book.Id}";
                    await Shell.Current.GoToAsync(route);
                }
            }
            finally { IsBusy = false; }
        }

        // =========================================================
        // 【防雷终结版】：搭载了智能降级与防盗链终极破解的极速下载管线
        // =========================================================
        private async Task DownloadCloudBookToLocalAsync(Book book)
        {
            try
            {
                var rule = JsonSerializer.Deserialize<BookSourceRuleModel>(book.ProgressLocator);
                string detailUrl = book.FileHash;

                BusyMessage = "正在解析TXT真实下载地址...";

                // 1. 获取TXT链接
                string txtUrl = await _searchEngine.GetDownloadUrlAsync(rule.RuleDetail, detailUrl);

                if (string.IsNullOrWhiteSpace(txtUrl))
                    throw new Exception("无法在网页中找到合法的 TXT 下载链接，网站可能不支持全本下载。");

                // 【绝杀 1：消除双重编码问题】将之前引擎里 Escape 过的字符串还原，让 HttpRequestMessage 去安全处理
                txtUrl = Uri.UnescapeDataString(txtUrl);

                BusyMessage = "正在极速下载全本文档...";

                using var handler = new HttpClientHandler
                {
                    AllowAutoRedirect = true,
                    UseCookies = true,
                    AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
                };
                using var httpClient = new HttpClient(handler);

                // 【绝杀 2：使用原生态 Uri 对象】
                var request = new HttpRequestMessage(HttpMethod.Get, new Uri(txtUrl));

                // 【绝杀 3：降级到 HTTP/1.1】很多老破小服务器不支持 HTTP/2 会报 403
                request.Version = new Version(1, 1);

                request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");

                // 用它主站的根域名来骗过防盗链
                var baseUri = new Uri(detailUrl);
                request.Headers.Add("Referer", $"{baseUri.Scheme}://{baseUri.Host}/");
                request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
                request.Headers.Add("Accept-Encoding", "gzip, deflate");

                var response = await httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    // 【绝杀 4：智能兜底 (迅雷模式)】如果带 Referer 被拒绝 (403)，尝试无 Referer 下载！
                    if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        LocalLogger.LogError("带 Referer 下载被 403 拦截，尝试触发无 Referer 的纯净迅雷模式...");

                        var fallbackRequest = new HttpRequestMessage(HttpMethod.Get, new Uri(txtUrl));
                        fallbackRequest.Version = new Version(1, 1);
                        fallbackRequest.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                        // 这一次什么花里胡哨的头都不带了，伪装成无情的下载器！
                        response = await httpClient.SendAsync(fallbackRequest);
                    }

                    // 如果还是失败，抛出真实异常
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new Exception($"服务器拒绝了下载 (错误码: {(int)response.StatusCode})。这通常是由于网站的深度防盗链机制拦截。");
                    }
                }

                var fileBytes = await response.Content.ReadAsByteArrayAsync();

                // 【安全校验】：如果下载下来的文件极小（不到1KB），说明下到的根本不是小说，而是网页防火墙的报错HTML代码
                if (fileBytes.Length < 1024)
                {
                    string errorText = System.Text.Encoding.UTF8.GetString(fileBytes);
                    LocalLogger.LogError($"下载数据过小，可能被防火墙拦截。内容: {errorText}");
                    throw new Exception("下载失败：文件数据异常，网站可能返回了拦截页面而不是真正的书籍文件。");
                }

                using var stream = new MemoryStream(fileBytes);

                BusyMessage = "正在本地极速拆解并生成目录...";

                string newFileName = $"{Guid.NewGuid()}.dat";
                string sandboxPath = await _fileSystemService.CopyFileToSandboxAsync(stream, newFileName);
                string trueHash = await _fileSystemService.CalculateFileHashAsync(sandboxPath);

                var parsedBook = await Task.Run(async () =>
                    await _bookParsingUseCase.ParseAndSplitBookAsync(sandboxPath, book.Title + ".txt", trueHash));

                parsedBook.FolderId = book.FolderId;
                parsedBook.Author = book.Author;
                parsedBook.Description = book.Description;
                parsedBook.CoverUrl = book.CoverUrl;

                // 清理占位书壳子
                try { book.IsDeleted = true; await _bookRepository.UpdateAsync(book); } catch { }
                await _bookRepository.ArchiveBookSafelyAsync(book.Id, false);

                // 将拆解好的新书连带章节一起 Insert 落库！
                await _bookRepository.SaveBookWithChaptersAsync(parsedBook, parsedBook.Chapters);

                await RefreshItemsCoreAsync();

                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    string route = $"{nameof(Views.ReaderPage)}?BookId={parsedBook.Id}";
                    await Shell.Current.GoToAsync(route);
                });
            }
            catch (Exception ex)
            {
                LocalLogger.LogError($"TXT极速下载失败: {ex.Message}", ex);
                MainThread.BeginInvokeOnMainThread(() => Shell.Current.DisplayAlert("下载失败", ex.Message, "知道了"));
            }
        }

        [RelayCommand]
        private async Task ImportBookAsync()
        {
            if (IsBusy) return;

            string action = await Shell.Current.DisplayActionSheetAsync(
                "选择获取书籍的方式", "取消", null, "全网搜书 (网络书源)", "本地导入 (TXT)", "坚果云导入 (TXT)");

            if (action == "本地导入 (TXT)") await ExecuteLocalImportAsync();
            else if (action == "坚果云导入 (TXT)") await ExecuteCloudImportStarterAsync();
            else if (action == "全网搜书 (网络书源)") await Shell.Current.GoToAsync("WebSearchPage");
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