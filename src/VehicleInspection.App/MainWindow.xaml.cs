using System.Windows;
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

            if (args.PropertyName == nameof(MainViewModel.IsChinese))
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

        dictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(_viewModel.IsChinese ? "Resources/Strings.zh-CN.xaml" : "Resources/Strings.en-US.xaml", UriKind.Relative)
        });
    }
}
