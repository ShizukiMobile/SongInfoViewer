using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace SongInfoViewer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            DataContext = _viewModel;

            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("[App] MainWindow Loaded. Initializing MediaSessionService...");
            await _viewModel.InitializeAsync();
        }
    }

    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly MediaSessionService _mediaSessionService = new();

        private string _songTitle = "読み込み中...";
        private string _artistName = "読み込み中...";
        private string _albumTitle = "";
        private ImageSource? _albumArt = null;

        public MainViewModel()
        {
            _mediaSessionService.SongInfoChanged += MediaSessionService_SongInfoChanged;
        }

        public async System.Threading.Tasks.Task InitializeAsync()
        {
            await _mediaSessionService.InitializeAsync();
        }

        private void MediaSessionService_SongInfoChanged(object? sender, SongInfo info)
        {
            // UIスレッドでプロパティを更新
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                SongTitle = info.Title;
                ArtistName = info.Artist;
                AlbumTitle = info.AlbumTitle;

                // 新しい画像がある場合、または「アイコンとして除外されたため既存の画像を維持する」指定がない場合に更新
                if (info.AlbumArt != null || !info.PreserveExistingArtOnNull)
                {
                    AlbumArt = info.AlbumArt;
                }
            });
        }

        public string SongTitle
        {
            get => _songTitle;
            set { _songTitle = value; OnPropertyChanged(); }
        }

        public string ArtistName
        {
            get => _artistName;
            set { _artistName = value; OnPropertyChanged(); }
        }

        public string AlbumTitle
        {
            get => _albumTitle;
            set { _albumTitle = value; OnPropertyChanged(); }
        }

        public ImageSource? AlbumArt
        {
            get => _albumArt;
            set { _albumArt = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class NullToVisibilityConverter : IValueConverter
    {
        public static readonly NullToVisibilityConverter Instance = new();

        public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
        {
            return value == null ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}