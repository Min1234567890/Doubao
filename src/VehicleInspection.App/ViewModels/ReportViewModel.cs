using System.Collections.ObjectModel;
using System.Windows;
using VehicleInspection.Application.Models;
using VehicleInspection.Application.Security;
using VehicleInspection.Application.Services;

namespace VehicleInspection.App.ViewModels;

public sealed class ReportViewModel : ViewModelBase
{
    private readonly InspectionService _inspectionService;
    private readonly ExportService _exportService;
    private readonly AccessControlService _accessControlService;
    private readonly UserSession _session;
    private DateTime? _fromDate = DateTime.Today.AddDays(-1);
    private DateTime? _toDate = DateTime.Today;
    private string _licensePlate = string.Empty;
    private InspectionStatus? _selectedStatus;
    private bool _fodAlertsOnly;
    private InspectionRecord? _selectedRecord;
    private string _securityMessage = string.Empty;

    public ReportViewModel(InspectionService inspectionService, ExportService exportService, AccessControlService accessControlService, UserSession session)
    {
        _inspectionService = inspectionService;
        _exportService = exportService;
        _accessControlService = accessControlService;
        _session = session;
        ApplyFiltersCommand = new RelayCommand(async _ => await ApplyFiltersAsync());
        ExportCsvCommand = new RelayCommand(async _ => await ExportCsvAsync(), _ => CanExport);
        ExportPdfCommand = new RelayCommand(async _ => await ExportPdfAsync(), _ => CanExport);
    }

    public ObservableCollection<InspectionRecord> Records { get; } = new();
    public IReadOnlyList<InspectionStatus> StatusOptions { get; } = Enum.GetValues<InspectionStatus>();
    public RelayCommand ApplyFiltersCommand { get; }
    public RelayCommand ExportCsvCommand { get; }
    public RelayCommand ExportPdfCommand { get; }
    public bool CanExport => _accessControlService.Can(_session.Role, Permission.ExportReports);

    public InspectionRecord? SelectedRecord
    {
        get => _selectedRecord;
        set => SetProperty(ref _selectedRecord, value);
    }

    public DateTime? FromDate
    {
        get => _fromDate;
        set => SetProperty(ref _fromDate, value);
    }

    public DateTime? ToDate
    {
        get => _toDate;
        set => SetProperty(ref _toDate, value);
    }

    public string LicensePlate
    {
        get => _licensePlate;
        set => SetProperty(ref _licensePlate, value);
    }

    public InspectionStatus? SelectedStatus
    {
        get => _selectedStatus;
        set => SetProperty(ref _selectedStatus, value);
    }

    public bool FodAlertsOnly
    {
        get => _fodAlertsOnly;
        set => SetProperty(ref _fodAlertsOnly, value);
    }

    public string SecurityMessage
    {
        get => _securityMessage;
        private set => SetProperty(ref _securityMessage, value);
    }

    public async Task ApplyFiltersAsync()
    {
        var filter = new ReportFilter
        {
            FromDate = FromDate,
            ToDate = ToDate,
            LicensePlate = LicensePlate,
            Status = SelectedStatus,
            FodAlertsOnly = FodAlertsOnly
        };

        var records = await _inspectionService.SearchReportsAsync(_session, filter);
        Records.Clear();
        foreach (var record in records)
        {
            Records.Add(record);
        }

        SelectedRecord = Records.FirstOrDefault();
        SecurityMessage = $"{Records.Count} records loaded. Export permission: {(CanExport ? "granted" : "denied")}.";
    }

    private async Task ExportCsvAsync()
    {
        try
        {
            var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), $"uvss-report-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
            await _exportService.ExportCsvAsync(_session, Records, path);
            SecurityMessage = $"CSV exported and audited: {path}";
        }
        catch (UnauthorizedAccessException ex)
        {
            SecurityMessage = ex.Message;
            MessageBox.Show(ex.Message, "RBAC", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task ExportPdfAsync()
    {
        try
        {
            var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), $"uvss-report-{DateTime.Now:yyyyMMdd-HHmmss}.pdf.txt");
            await _exportService.ExportPdfManifestAsync(_session, Records, path);
            SecurityMessage = $"PDF manifest exported and audited: {path}";
        }
        catch (UnauthorizedAccessException ex)
        {
            SecurityMessage = ex.Message;
            MessageBox.Show(ex.Message, "RBAC", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
