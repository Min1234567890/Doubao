using System.Net;
using System.Windows;
using VehicleInspection.App.Controls;
using VehicleInspection.App.Localization;
using VehicleInspection.App.Services;
using VehicleInspection.Application.Models;
using VehicleInspection.Application.Security;
using VehicleInspection.Application.Services;

namespace VehicleInspection.App.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly UserSession _session;
    private readonly AuditService _auditService;
    private readonly WindowsAuthenticationResult _authenticationResult;
    private readonly TcpDeviceSocketListener _socketListener;
    private string _activeView = "Dashboard";
    private string _socketStatus = Loc.Get("SocketStopped");
    private int _languageIndex;

    public MainViewModel()
    {
        var backendClient = new BackendInspectionClient(Environment.GetEnvironmentVariable("UVSS_BACKEND_URL") ?? "http://localhost:5077");
        var repository = new HttpInspectionRepository(backendClient);
        var auditService = new AuditService(repository);
        _auditService = auditService;
        var accessControlService = new AccessControlService();
        var authenticationService = new WindowsAuthenticationService();
        _authenticationResult = authenticationService.AuthenticateCurrentUser(new WindowsAuthenticationOptions
        {
            ActiveDirectoryDomain = Environment.GetEnvironmentVariable("UVSS_AD_DOMAIN"),
            ActiveDirectoryServer = Environment.GetEnvironmentVariable("UVSS_AD_SERVER")
        });
        var inspectionService = new InspectionService(repository, auditService);
        var exportService = new ExportService(auditService, accessControlService);
        var ingestionForwarder = new FrontendDeviceIngestionForwarder(backendClient, Environment.GetEnvironmentVariable("UVSS_DEVICE_API_KEY") ?? "development-key-change-me");
        _socketListener = new TcpDeviceSocketListener(ingestionForwarder, IPAddress.Loopback, 47011, 10 * 1024 * 1024);
        _socketListener.StatusChanged += (_, status) => SocketStatus = status;
        ingestionForwarder.MessageIgnored += (_, message) => SocketStatus = message;
        _session = _authenticationResult.Session;
        Dashboard = new DashboardViewModel(inspectionService, _session);
        Reports = new ReportViewModel(inspectionService, exportService, accessControlService, _session);
        ingestionForwarder.InspectionUpdated += (_, inspection) => System.Windows.Application.Current.Dispatcher.Invoke(() => Dashboard.ApplyInspection(inspection));
        ShowDashboardCommand = new RelayCommand(_ => ActiveView = "Dashboard");
        ShowReportsCommand = new RelayCommand(_ => ActiveView = "Reports");
        ToggleLanguageCommand = new RelayCommand(_ => ToggleLanguage());
        ExitCommand = new RelayCommand(_ => Exit());
    }

    public DashboardViewModel Dashboard { get; }
    public ReportViewModel Reports { get; }
    public RelayCommand ShowDashboardCommand { get; }
    public RelayCommand ShowReportsCommand { get; }
    public RelayCommand ToggleLanguageCommand { get; }
    public RelayCommand ExitCommand { get; }
    public string UserName => _session.UserName;
    public string RoleName => _session.Role.ToString();
    public string AuthenticationProvider => _session.AuthenticationProvider;
    public DateTimeOffset LoginTime => _session.LoginTime;

    public string SocketStatus
    {
        get => _socketStatus;
        private set => SetProperty(ref _socketStatus, value);
    }
    public bool IsDashboardActive => ActiveView == "Dashboard";
    public bool IsReportsActive => ActiveView == "Reports";

    public string ActiveView
    {
        get => _activeView;
        set
        {
            if (SetProperty(ref _activeView, value))
            {
                _session.Touch();
                OnPropertyChanged(nameof(IsDashboardActive));
                OnPropertyChanged(nameof(IsReportsActive));
            }
        }
    }

    public int LanguageIndex
    {
        get => _languageIndex;
        private set
        {
            if (SetProperty(ref _languageIndex, value))
            {
                OnPropertyChanged(nameof(IsEnglish));
                OnPropertyChanged(nameof(IsArabic));
                OnPropertyChanged(nameof(IsMalay));
                OnPropertyChanged(nameof(IsThai));
                OnPropertyChanged(nameof(LanguageLabel));
            }
        }
    }

    public bool IsEnglish => _languageIndex == 0;
    public bool IsArabic => _languageIndex == 1;
    public bool IsMalay => _languageIndex == 2;
    public bool IsThai => _languageIndex == 3;
    public string LanguageLabel => _languageIndex switch
    {
        0 => "عربي",
        1 => "Melayu",
        2 => "ไทย",
        3 => "English",
        _ => "عربي"
    };

    public async Task InitializeAsync()
    {
        await _auditService.RecordAsync(_session, "Login", _authenticationResult.Mode.ToString(), _authenticationResult.ResultDetail);
        await Dashboard.LoadAsync();
        await Reports.ApplyFiltersAsync();
        await _socketListener.StartAsync();
    }

    public async Task ShutdownAsync()
    {
        await _auditService.RecordAsync(_session, "Logout", _session.AuthenticationProvider, "Success");
        await _socketListener.StopAsync();
    }

    private void ToggleLanguage()
    {
        LanguageIndex = (_languageIndex + 1) % 4;
        _session.Touch();
    }

    private void Exit()
    {
        if (DarkDialog.Show(
                System.Windows.Application.Current.MainWindow,
                Loc.Get("Exit"),
                Loc.Get("ExitConfirm")))
        {
            System.Windows.Application.Current.Shutdown();
        }
    }

}
