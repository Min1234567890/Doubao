using System.Windows.Media;
using VehicleInspection.Application.Models;
using VehicleInspection.Application.Services;

namespace VehicleInspection.App.ViewModels;

public sealed class DashboardViewModel : ViewModelBase
{
    private readonly InspectionService _inspectionService;
    private readonly UserSession _session;
    private InspectionRecord? _currentInspection;

    public DashboardViewModel(InspectionService inspectionService, UserSession session)
    {
        _inspectionService = inspectionService;
        _session = session;
    }

    public InspectionRecord? CurrentInspection
    {
        get => _currentInspection;
        private set => SetProperty(ref _currentInspection, value);
    }

    public string XrayFodMode => CurrentInspection?.HasXray == true ? "X-ray image available" : "FOD detection active";
    public IReadOnlyList<SystemErrorMessage> SystemErrors => CurrentInspection?.SystemErrors ?? Array.Empty<SystemErrorMessage>();
    public int CriticalErrorCount => SystemErrors.Count(error => error.Severity == SystemErrorSeverity.Critical);
    public int WarningErrorCount => SystemErrors.Count(error => error.Severity == SystemErrorSeverity.Warning);
    public string SystemErrorSummary => SystemErrors.Count == 0 ? "All subsystems operational" : $"{CriticalErrorCount} critical / {SystemErrors.Count} total subsystem alerts";
    public Brush InspectionStatusBrush => CurrentInspection?.Status switch
    {
        InspectionStatus.Clear => SuccessBrush,
        InspectionStatus.Pending or InspectionStatus.Review => WarningBrush,
        InspectionStatus.Hold or InspectionStatus.Escalated => DangerBrush,
        _ => WarningBrush
    };
    public Brush SystemStatusBrush => CriticalErrorCount > 0 ? DangerBrush : WarningErrorCount > 0 ? WarningBrush : SuccessBrush;

    private static Brush SuccessBrush { get; } = new SolidColorBrush(Color.FromRgb(83, 193, 138));
    private static Brush WarningBrush { get; } = new SolidColorBrush(Color.FromRgb(231, 184, 77));
    private static Brush DangerBrush { get; } = new SolidColorBrush(Color.FromRgb(227, 93, 91));

    public async Task LoadAsync()
    {
        ApplyInspection(await _inspectionService.GetCurrentInspectionAsync(_session));
    }

    public void ApplyInspection(InspectionRecord inspection)
    {
        CurrentInspection = inspection;
        OnPropertyChanged(nameof(XrayFodMode));
        OnPropertyChanged(nameof(SystemErrors));
        OnPropertyChanged(nameof(CriticalErrorCount));
        OnPropertyChanged(nameof(WarningErrorCount));
        OnPropertyChanged(nameof(SystemErrorSummary));
        OnPropertyChanged(nameof(InspectionStatusBrush));
        OnPropertyChanged(nameof(SystemStatusBrush));
    }
}
