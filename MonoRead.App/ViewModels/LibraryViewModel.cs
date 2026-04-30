using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MonoRead.App.Messages;
using MonoRead.Core.Entities;
using MonoRead.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;

namespace MonoRead.App.ViewModels
{
    // 必须是 partial class，因为 Toolkit 会自动帮我们生成底层代码
    public partial class LibraryViewModel : ObservableObject
    {
        private readonly IFileSystemService _fileSystemService;
        private readonly IBookParsingUseCase _bookParsingUseCase;
        private readonly IBookRepository _bookRepository;
        // 构造函数注入我们刚刚写的服务
        // MVVM 核心：驱动界面列表的数据源，增删元素会自动触发 UI 刷新
        [ObservableProperty]
        private ObservableCollection<Book> _books = new();

        public LibraryViewModel(
            IFileSystemService fileSystemService,
            IBookParsingUseCase bookParsingUseCase,
            IBookRepository bookRepository)
        {
            _fileSystemService = fileSystemService;
            _bookParsingUseCase = bookParsingUseCase;
            _bookRepository = bookRepository;

            // 视图模型被创建时，自动从 SQLite 加载已有书籍
            LoadBooksCommand.Execute(null);

            // 【新增：监听外部传入的文件】
            // 在构造函数里：
            WeakReferenceMessenger.Default.Register<FileImportMessage>(this, async (r, m) =>
            {
                await ProcessImportedFileAsync(m.SandboxPath, m.FileName);
            });
        }

        // 加载书架数据的命令
        [RelayCommand]
        private async Task LoadBooksAsync()
        {
            try
            {
                // 1. 在后台线程去 SQLite 查数据，不卡顿
                var bookList = await _bookRepository.GetAllBooksAsync();

                // 2. 【核心修复】强制回到主线程更新 UI 绑定的集合！
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    Books.Clear();
                    foreach (var book in bookList)
                    {
                        Books.Add(book);
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"加载书架异常: {ex.Message}");
            }
        }

        // 导入书籍的核心命令
        [RelayCommand]
        private async Task ImportBookAsync()
        {
            try
            {
                var customFileType = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.Android, new[] { "text/plain" } },
                { DevicePlatform.WinUI, new[] { ".txt" } },
                { DevicePlatform.MacCatalyst, new[] { "public.plain-text" } }
            });

                var result = await FilePicker.Default.PickAsync(new PickOptions
                {
                    PickerTitle = "请选择要导入的小说 (TXT)",
                    FileTypes = customFileType
                });

                if (result == null) return;

                // 1. 索要安全流并拷贝到沙盒 (此时在后台线程，安全)
                var newBookId = Guid.NewGuid();
                string fileName = result.FileName;
                string newFileName = $"{newBookId}{Path.GetExtension(fileName)}";

                using var stream = await result.OpenReadAsync();
                string savedPath = await _fileSystemService.CopyFileToSandboxAsync(stream, newFileName);

                // 2. 计算防重哈希
                string fileHash = await _fileSystemService.CalculateFileHashAsync(savedPath);

                // 3. 呼叫百万字级核心解析引擎，执行切割与入库！
                var parsedBook = await _bookParsingUseCase.ParseAndSplitBookAsync(savedPath, fileName, fileHash);

                // 4. 【核心修复】解析完成后，强制回到主线程操作界面！
                if (Application.Current?.MainPage != null)
                {
                    await Application.Current.MainPage.Dispatcher.DispatchAsync(async () =>
                    {
                        // 插入到界面列表的最前面，UI 瞬间响应
                        Books.Insert(0, parsedBook);

                        // 弹出成功提示
                        await Application.Current.MainPage.DisplayAlert(
                            "导入成功",
                            $"《{parsedBook.Title}》已成功加入书架并完成目录解析！",
                            "太棒了");
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"导入异常: {ex.Message}");
                if (Application.Current?.MainPage != null)
                {
                    await Application.Current.MainPage.Dispatcher.DispatchAsync(async () =>
                    {
                        await Application.Current.MainPage.DisplayAlert("导入失败", ex.Message, "确定");
                    });
                }
            }
        }




        // 当用户点击某本书时触发
        [RelayCommand]
        private async Task OpenBookAsync(Book selectedBook)
        {
            if (selectedBook == null) return;
            // 【核心修复】使用路由字典传递强类型参数，完美解决 String 无法转 Guid 的问题
            var navigationParameter = new Dictionary<string, object>
        {
            { "BookId", selectedBook.Id }
        };
            // 携带 BookId 路由跳转到阅读器页面
            await Shell.Current.GoToAsync($"{nameof(Views.ReaderPage)}?BookId={selectedBook.Id}");
        }

        // 【核心新增：处理外部传入的数据流】
        private async Task ProcessImportedStreamAsync(Stream stream, string fileName)
        {
            try
            {
                var newBookId = Guid.NewGuid();
                string newFileName = $"{newBookId}.dat"; // 强制重命名，隔离外部名

                // 1. 拷贝到私有沙盒 (消耗流)
                string savedPath = await _fileSystemService.CopyFileToSandboxAsync(stream, newFileName);

                // 2. 计算防重哈希
                string fileHash = await _fileSystemService.CalculateFileHashAsync(savedPath);

                // 3. 呼叫百万字级核心解析引擎
                var parsedBook = await _bookParsingUseCase.ParseAndSplitBookAsync(savedPath, fileName, fileHash);

                // 4. 强制回到主线程操作界面
                if (Application.Current?.MainPage != null)
                {
                    await Application.Current.MainPage.Dispatcher.DispatchAsync(async () =>
                    {
                        Books.Insert(0, parsedBook);
                        await Application.Current.MainPage.DisplayAlert(
                            "导入成功",
                            $"《{parsedBook.Title}》已成功加入书架！",
                            "开始阅读");
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"处理外部流异常: {ex.Message}");
                if (Application.Current?.MainPage != null)
                {
                    await Application.Current.MainPage.Dispatcher.DispatchAsync(async () =>
                    {
                        await Application.Current.MainPage.DisplayAlert("导入失败", ex.Message, "确定");
                    });
                }
            }
            finally
            {
                // 确保释放底层 Android 的流
                stream?.Dispose();
            }
        }

        // 新的处理方法：
        private async Task ProcessImportedFileAsync(string sandboxPath, string fileName)
        {
            try
            {
                // 文件已经在沙盒里了，直接算 Hash 并解析！
                string fileHash = await _fileSystemService.CalculateFileHashAsync(sandboxPath);
                var parsedBook = await _bookParsingUseCase.ParseAndSplitBookAsync(sandboxPath, fileName, fileHash);

                if (Application.Current?.MainPage != null)
                {
                    await Application.Current.MainPage.Dispatcher.DispatchAsync(async () =>
                    {
                        LoadBooksCommand.Execute(null); // 刷新书架
                        await Application.Current.MainPage.DisplayAlert("导入成功", $"《{parsedBook.Title}》已成功加入书架！", "开始阅读");
                    });
                }
            }
            catch (Exception ex)
            {
                // 如果解析失败，把刚才 Android 层临时拷贝的死文件删掉防垃圾
                if (File.Exists(sandboxPath)) File.Delete(sandboxPath);
                // 显示报错...
            }
        }

    }
}
