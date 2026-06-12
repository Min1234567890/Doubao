using System.Windows;
using VehicleInspection.App.Localization;
using VehicleInspection.App.ViewModels;

namespace VehicleInspection.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += OnLoaded;
        Closing += OnClosing;
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.ActiveView))
            {
                UpdateActiveView();
            }

            if (args.PropertyName == nameof(MainViewModel.LanguageIndex))
            {
                UpdateLanguage();
            }
        };
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
        UpdateActiveView();
    }

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        await _viewModel.ShutdownAsync();
    }

    private void UpdateActiveView()
    {
        DashboardContent.Visibility = _viewModel.IsDashboardActive ? Visibility.Visible : Visibility.Collapsed;
        ReportContent.Visibility = _viewModel.IsReportsActive ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateLanguage()
    {
        var dictionaries = System.Windows.Application.Current.Resources.MergedDictionaries;
        var oldDictionary = dictionaries.FirstOrDefault(dictionary => dictionary.Source != null && dictionary.Source.OriginalString.StartsWith("Resources/Strings", StringComparison.OrdinalIgnoreCase));
        if (oldDictionary != null)
        {
            dictionaries.Remove(oldDictionary);
        }

        var source = _viewModel.LanguageIndex switch
        {
            1 => "Resources/Strings.ar-SA.xaml",
            2 => "Resources/Strings.ms-MY.xaml",
            3 => "Resources/Strings.th-TH.xaml",
            _ => "Resources/Strings.en-US.xaml"
        };

        dictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(source, UriKind.Relative)
        });

        Loc.NotifyLanguageChanged();
    }
}
