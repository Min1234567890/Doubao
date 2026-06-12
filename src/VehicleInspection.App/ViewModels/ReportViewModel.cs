using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using VehicleInspection.App.Localization;
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
        ExportCurrentRecordCommand = new RelayCommand(async _ => await ExportCurrentRecordAsync(), _ => CanExportCurrentRecord);
        Loc.LanguageChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(StatusFilterOptions));
            OnPropertyChanged(nameof(StatusFilterText));
        };
    }

    public ObservableCollection<InspectionRecord> Records { get; } = new();
    public IReadOnlyList<InspectionStatus> StatusOptions { get; } = Enum.GetValues<InspectionStatus>();
    public IReadOnlyList<string> StatusFilterOptions => new[] { Loc.Get("StatusAll") }.Concat(StatusDisplayConverter.GetDisplayOptions()).ToList();

    private string _statusFilterText = Loc.Get("StatusAll");
    public string StatusFilterText
    {
        get => _statusFilterText;
        set
        {
            if (!SetProperty(ref _statusFilterText, value)) return;
            if (value == Loc.Get("StatusAll")) { SelectedStatus = null; return; }
            SelectedStatus = (InspectionStatus?)new StatusDisplayConverter().ConvertBack(value, typeof(InspectionStatus), null!, System.Globalization.CultureInfo.CurrentCulture);
        }
    }

    public RelayCommand ApplyFiltersCommand { get; }
    public RelayCommand ExportCsvCommand { get; }
    public RelayCommand ExportPdfCommand { get; }
    public RelayCommand ExportCurrentRecordCommand { get; }
    public bool CanExport => _accessControlService.Can(_session.Role, Permission.ExportReports);
    public bool CanExportCurrentRecord => CanExport && SelectedRecord is not null;
    public bool HasSelectedUvssImage => !string.IsNullOrWhiteSpace(SelectedRecord?.UnderVehicleImagePath);

    private int _sensitivityLevel = 5;
    public int SensitivityLevel
    {
        get => _sensitivityLevel;
        set => SetProperty(ref _sensitivityLevel, value);
    }

    public InspectionRecord? SelectedRecord
    {
        get => _selectedRecord;
        set
        {
            if (SetProperty(ref _selectedRecord, value))
            {
                ExportCurrentRecordCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(HasSelectedUvssImage));
            }
        }
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
        SecurityMessage = Loc.Format("RecordsLoaded", Records.Count, CanExport ? "granted" : "denied");
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

    public async Task UpdateLicensePlateAsync(InspectionRecord record, string oldPlate, string newPlate)
    {
        if (string.IsNullOrWhiteSpace(newPlate))
            return;

        if (string.Equals(oldPlate, newPlate, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            await _inspectionService.UpdateLicensePlateAsync(
                _session, record.Id, oldPlate, newPlate);

            record.LicensePlate = newPlate;
            record.LicensePlateHash = ComputeHashForLocal(newPlate);

            // Refresh the DataGrid by replacing the record in the collection
            var index = Records.IndexOf(record);
            if (index >= 0)
            {
                Records[index] = record;
            }

            SecurityMessage = Loc.Format("LicensePlateUpdatedMsg", oldPlate, newPlate);
        }
        catch (Exception ex)
        {
            SecurityMessage = Loc.Format("UpdateFailed", ex.Message);
        }
    }

    public async Task UpdateNotesAsync(InspectionRecord record, string notes)
    {
        try
        {
            await _inspectionService.UpdateNotesAsync(_session, record.Id, notes);
            record.Notes = notes;
            var index = Records.IndexOf(record);
            if (index >= 0) Records[index] = record;
        }
        catch (Exception ex)
        {
            SecurityMessage = Loc.Format("UpdateFailed", ex.Message);
        }
    }

    public async Task UpdateInspectionStatusAsync(InspectionRecord record, InspectionStatus oldStatus, InspectionStatus newStatus)
    {
        if (oldStatus == newStatus)
            return;

        try
        {
            await _inspectionService.UpdateInspectionStatusAsync(_session, record.Id, oldStatus, newStatus);
            record.Status = newStatus;

            var index = Records.IndexOf(record);
            if (index >= 0)
            {
                Records[index] = record;
            }

            SecurityMessage = Loc.Format("StatusUpdatedMsg", oldStatus, newStatus);
        }
        catch (Exception ex)
        {
            SecurityMessage = Loc.Format("UpdateFailed", ex.Message);
        }
    }

    private static string ComputeHashForLocal(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.ToUpperInvariant()));
        return Convert.ToHexString(bytes);
    }

    private async Task ExportPdfAsync()
    {
        try
        {
            var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), $"uvss-report-{DateTime.Now:yyyyMMdd-HHmmss}.pdf");
            await _exportService.ExportPdfAsync(_session, Records, path);
            SecurityMessage = $"PDF exported: {path}";
        }
        catch (UnauthorizedAccessException ex)
        {
            SecurityMessage = ex.Message;
            MessageBox.Show(ex.Message, "RBAC", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task ExportCurrentRecordAsync()
    {
        if (SelectedRecord is null)
        {
            SecurityMessage = Loc.Get("NoRecordSelectedForExport");
            return;
        }

        try
        {
            var safePlate = SelectedRecord.LicensePlate?.Replace(" ", "_") ?? "unknown";
            var path = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                $"uvss-single-{safePlate}-{DateTime.Now:yyyyMMdd-HHmmss}.pdf");
            await _exportService.ExportCurrentRecordPdfAsync(_session, SelectedRecord, path, SensitivityLevel);
            SecurityMessage = $"Single-record PDF exported: {path}";
        }
        catch (UnauthorizedAccessException ex)
        {
            SecurityMessage = ex.Message;
            MessageBox.Show(ex.Message, "RBAC", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
