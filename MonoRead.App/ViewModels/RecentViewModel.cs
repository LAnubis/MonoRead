using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonoRead.Core.Entities;
using MonoRead.Core.Interfaces;
using System.Collections.ObjectModel;

namespace MonoRead.App.ViewModels
{
    public partial class RecentViewModel : ObservableObject
    {
        private readonly IBookRepository _bookRepository;

        [ObservableProperty]
        private ObservableCollection<Book> _recentBooks = new();

        [ObservableProperty]
        private bool _isBusy;

        public RecentViewModel(IBookRepository bookRepository)
        {
            _bookRepository = bookRepository;
        }

        [RelayCommand]
        public async Task LoadRecentBooksAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                var allBooks = await _bookRepository.GetAllBooksAsync();

                // 核心逻辑：找出没被删，且 UpdatedAt 在 3 天内的书，倒序排列
                var limitDate = DateTime.UtcNow.AddDays(-3);
                var recent = allBooks
                    .Where(b => !b.IsDeleted && b.UpdatedAt >= limitDate)
                    .OrderByDescending(b => b.UpdatedAt)
                    .ToList();

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    RecentBooks.Clear();
                    foreach (var book in recent) RecentBooks.Add(book);
                });
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task OpenBookAsync(Book book)
        {
            if (book == null) return;
            string route = $"{nameof(Views.ReaderPage)}?BookId={book.Id}";
            await Shell.Current.GoToAsync(route);
        }
    }
}
