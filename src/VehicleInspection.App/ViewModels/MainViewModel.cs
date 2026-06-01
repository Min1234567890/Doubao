using System.Net;
using System.Windows;
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
    private readonly SessionLockService _sessionLockService;
    private readonly TcpDeviceSocketListener _socketListener;
    private string _activeView = "Dashboard";
    private string _socketStatus = "Socket listener stopped";
    private bool _isChinese;

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
        _sessionLockService = new SessionLockService();
        _session = _authenticationResult.Session;
        Dashboard = new DashboardViewModel(inspectionService, _session);
        Reports = new ReportViewModel(inspectionService, exportService, accessControlService, _session);
        ingestionForwarder.InspectionUpdated += (_, inspection) => System.Windows.Application.Current.Dispatcher.Invoke(() => Dashboard.ApplyInspection(inspection));
        ShowDashboardCommand = new RelayCommand(_ => ActiveView = "Dashboard");
        ShowReportsCommand = new RelayCommand(_ => ActiveView = "Reports");
        ToggleLanguageCommand = new RelayCommand(_ => ToggleLanguage());
        LockCommand = new RelayCommand(_ => LockSession());
    }

    public DashboardViewModel Dashboard { get; }
    public ReportViewModel Reports { get; }
    public RelayCommand ShowDashboardCommand { get; }
    public RelayCommand ShowReportsCommand { get; }
    public RelayCommand ToggleLanguageCommand { get; }
    public RelayCommand LockCommand { get; }
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

    public bool IsChinese
    {
        get => _isChinese;
        private set => SetProperty(ref _isChinese, value);
    }

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
        IsChinese = !IsChinese;
        _session.Touch();
    }

    private void LockSession()
    {
        _session.Lock();
        OnPropertyChanged(nameof(SessionState));
    }

    public string SessionState => _session.IsLocked ? "Locked" : _sessionLockService.ShouldLock(_session, DateTimeOffset.UtcNow) ? "Idle lock pending" : "Active";
}
