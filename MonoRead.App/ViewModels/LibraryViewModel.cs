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
        // 【新增 1：Loading 状态机】
        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private string _busyMessage = "正在处理...";

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
            if (IsBusy) return;

            try
            {
                // 1. 唤起系统原生文件选择器
                var result = await FilePicker.Default.PickAsync(new PickOptions
                {
                    PickerTitle = "请选择TXT小说文件"
                });

                if (result != null)
                {
                    // 2. 拿到文件后，立刻开启 Loading 状态
                    IsBusy = true;
                    BusyMessage = "正在极速拆解小说章节，请稍候...";

                    // 【核心修复：强制让出主线程】给 MAUI 渲染引擎 50 毫秒的时间，把黑色的 Loading 遮罩画出来！
                    await Task.Delay(50);

                    // 【核心修复：绝对线程隔离】把 I/O 拷贝和正则解析踢入后台线程
                    var parsedBook = await Task.Run(async () =>
                    {
                        // A. 将选中的文件流拷贝到沙盒
                        using var stream = await result.OpenReadAsync();
                        string newFileName = $"{Guid.NewGuid()}.dat";
                        string sandboxPath = await _fileSystemService.CopyFileToSandboxAsync(stream, newFileName);

                        // B. 执行哈希和百万字解析
                        string fileHash = await _fileSystemService.CalculateFileHashAsync(sandboxPath);
                        return await _bookParsingUseCase.ParseAndSplitBookAsync(sandboxPath, result.FileName, fileHash);
                    });

                    // 3. 带着成果回到主线程更新 UI
                    MainThread.BeginInvokeOnMainThread(async () =>
                    {
                        LoadBooksCommand.Execute(null); // 刷新书架
                        await OpenBookAsync(parsedBook); // 自动打开阅读器
                    });
                }
            }
            catch (Exception ex)
            {
                MainThread.BeginInvokeOnMainThread(() => Application.Current.MainPage.DisplayAlert("导入失败", ex.Message, "确定"));
            }
            finally
            {
                IsBusy = false; // 无论成功失败，关闭 Loading
            }
        }




        // 当用户点击某本书时触发
        [RelayCommand]
        private async Task OpenBookAsync(Book selectedBook)
        {
            if (selectedBook == null || IsBusy) return;

            IsBusy = true; // 锁定，防止疯狂连点
            try
            {
                // 【灵魂一击】：强制等待 50 毫秒！
                // 让 Android 底层的 CollectionView 水波纹动画和手势结算完毕，再执行页面路由，彻底消灭底层崩溃！
                await Task.Delay(50);

                // 严格使用字符串字典传参
                var navParams = new Dictionary<string, object>
            {
                { "BookId", selectedBook.Id.ToString() }
            };

                await Shell.Current.GoToAsync(nameof(Views.ReaderPage), navParams);
            }
            finally
            {
                IsBusy = false; // 解锁
            }
        }


        // 【新增 2：主动检查冷启动文件的命令】
        [RelayCommand]
        private async Task CheckPendingImportAsync()
        {
            if (!string.IsNullOrEmpty(App.PendingImportFilePath))
            {
                string path = App.PendingImportFilePath;
                string name = App.PendingImportFileName ?? "未知文档";

                // 清空静态变量，防止重复导入
                App.PendingImportFilePath = null;
                App.PendingImportFileName = null;

                await ProcessImportedFileAsync(path, name);
            }
        }
     

        // 新的处理方法：
        private async Task ProcessImportedFileAsync(string sandboxPath, string fileName)
        {
            if (IsBusy) return;
            IsBusy = true;
            BusyMessage = "正在极速拆解小说章节，请稍候...";

            // 强行让出主线程，确保 Loading 遮罩有足够的时间被渲染在屏幕上
            await Task.Delay(50);

            try
            {
                // 【核心修复：绝对线程隔离】
                // 将耗时的 Hash 计算和百兆级文本正则解析，彻彻底底扔进 Task.Run 后台线程
                // 绝不允许它们占用一毫秒的 UI 线程，彻底消灭转圈卡死和 ANR 崩溃！
                var parsedBook = await Task.Run(async () =>
                {
                    string fileHash = await _fileSystemService.CalculateFileHashAsync(sandboxPath);
                    return await _bookParsingUseCase.ParseAndSplitBookAsync(sandboxPath, fileName, fileHash);
                });

                // 苦力活干完，带着解析好的数据，安全回到主线程刷新界面
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    LoadBooksCommand.Execute(null); // 刷新书架
                    await OpenBookAsync(parsedBook); // 自动打开它
                });
            }
            catch (Exception ex)
            {
                if (File.Exists(sandboxPath)) File.Delete(sandboxPath);
                MainThread.BeginInvokeOnMainThread(() => Application.Current.MainPage.DisplayAlert("导入失败", ex.Message, "确定"));
            }
            finally
            {
                IsBusy = false;
            }
        }

    }
}
