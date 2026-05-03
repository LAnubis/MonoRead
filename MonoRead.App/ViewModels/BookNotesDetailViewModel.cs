using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonoRead.Core.Entities;
using System.Collections.ObjectModel;

namespace MonoRead.App.ViewModels
{
    [QueryProperty(nameof(BookIdString), "BookId")]
    [QueryProperty(nameof(BookTitle), "BookTitle")]
    public partial class BookNotesDetailViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _bookIdString = string.Empty;

        [ObservableProperty]
        private string _bookTitle = string.Empty;

        [ObservableProperty]
        private ObservableCollection<BookNote> _bookNotesList = new();

        partial void OnBookIdStringChanged(string value)
        {
            if (Guid.TryParse(value, out Guid bookId))
            {
                LoadNotesForBook(bookId);
            }
        }

        private void LoadNotesForBook(Guid bookId)
        {
            // 模拟数据库查询：WHERE BookId = bookId ORDER BY CreatedAt DESC
            // var notes = await _noteRepo.GetNotesByBookAsync(bookId);
            var notes = new List<BookNote>(); // 替换为真实调用

            MainThread.BeginInvokeOnMainThread(() =>
            {
                BookNotesList.Clear();
                foreach (var note in notes) BookNotesList.Add(note);
            });
        }

        [RelayCommand]
        private async Task GoBackAsync() => await Shell.Current.GoToAsync("..");
    }
}