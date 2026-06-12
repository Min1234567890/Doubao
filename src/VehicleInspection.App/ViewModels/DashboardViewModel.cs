using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using VehicleInspection.App.Controls;
using VehicleInspection.App.Localization;
using VehicleInspection.Application.Models;
using VehicleInspection.Application.Services;

namespace VehicleInspection.App.ViewModels;

public sealed class DashboardViewModel : ViewModelBase
{
    private readonly InspectionService _inspectionService;
    private readonly UserSession _session;
    private InspectionRecord? _currentInspection;
    private InspectionRecord? _previousInspection;
    private int _sensitivityLevel = 3;
    private List<RoiEntry> _allRois = new();

    private static readonly string RoiJsonPath = @"D:\image\transaction\ROI.json";

    // Reference canvas dimensions (ZoomPanImageControl default)
    private const double CanvasWidth = 900;
    private const double CanvasHeight = 460;

    // Source image dimensions that the ROI coordinates are based on
    private const double ImageWidth = 8192;
    private const double ImageHeight = 4096;

    public DashboardViewModel(InspectionService inspectionService, UserSession session)
    {
        _inspectionService = inspectionService;
        _session = session;
        FodRegions = new ObservableCollection<FodRegion>();
        Loc.LanguageChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(StatusDisplayText));
            OnPropertyChanged(nameof(StatusDisplayOptions));
            OnPropertyChanged(nameof(StatusSummary));
            OnPropertyChanged(nameof(FodSummary));
            OnPropertyChanged(nameof(SystemErrorSummary));
            OnPropertyChanged(nameof(SensitivityLabel));
            OnPropertyChanged(nameof(XrayFodMode));
        };
    }

    public InspectionRecord? CurrentInspection
    {
        get => _currentInspection;
        private set => SetProperty(ref _currentInspection, value);
    }

    public InspectionRecord? PreviousInspection
    {
        get => _previousInspection;
        private set
        {
            if (SetProperty(ref _previousInspection, value))
            {
                OnPropertyChanged(nameof(PreviousUnderVehicleImagePath));
                OnPropertyChanged(nameof(HasPreviousScan));
            }
        }
    }

    // ── Image paths ──────────────────────────────────────────
    public string? CurrentUnderVehicleImagePath => CurrentInspection?.UnderVehicleImagePath;
    public string? PreviousUnderVehicleImagePath => PreviousInspection?.UnderVehicleImagePath;
    public bool HasPreviousScan => PreviousInspection is not null;
    public string? XrayImagePath => CurrentInspection?.XrayImagePath;
    public string? LicensePlateImagePath => CurrentInspection?.LicensePlateImagePath;

    // ── Status ────────────────────────────────────────────────
    public IReadOnlyList<InspectionStatus> StatusOptions { get; } = Enum.GetValues<InspectionStatus>();
    public IReadOnlyList<string> StatusDisplayOptions => StatusDisplayConverter.GetDisplayOptions();

    private InspectionStatus _selectedStatus;
    private bool _suppressStatusSync;

    public string StatusDisplayText
    {
        get => Loc.Get("Status" + _selectedStatus);
        set
        {
            var result = new StatusDisplayConverter().ConvertBack(value, typeof(InspectionStatus), null!, System.Globalization.CultureInfo.CurrentCulture);
            var newStatus = result is InspectionStatus s ? s : InspectionStatus.Pending;
            if (newStatus == _selectedStatus) return;
            _selectedStatus = newStatus;
            OnPropertyChanged(nameof(StatusDisplayText));
            if (_suppressStatusSync) return;
            _ = UpdateStatusAsync(newStatus);
        }
    }

    private async Task UpdateStatusAsync(InspectionStatus newStatus)
    {
        if (CurrentInspection is null || CurrentInspection.Status == newStatus)
            return;

        var oldStatus = CurrentInspection.Status;
        try
        {
            await _inspectionService.UpdateInspectionStatusAsync(_session, CurrentInspection.Id, oldStatus, newStatus);
            CurrentInspection.Status = newStatus;
            OnPropertyChanged(nameof(StatusSummary));
            OnPropertyChanged(nameof(InspectionStatusBrush));
        }
        catch
        {
            _suppressStatusSync = true;
            _selectedStatus = oldStatus;
            OnPropertyChanged(nameof(StatusDisplayText));
            _suppressStatusSync = false;
        }
    }

    public async Task UpdateNotesAsync(string notes)
    {
        if (CurrentInspection is null) return;
        try
        {
            await _inspectionService.UpdateNotesAsync(_session, CurrentInspection.Id, notes);
            CurrentInspection.Notes = notes;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Notes update failed: {ex.Message}");
        }
    }

    // ── Sensitivity slider ───────────────────────────────────
    public int SensitivityLevel
    {
        get => _sensitivityLevel;
        set
        {
            if (SetProperty(ref _sensitivityLevel, value))
            {
                OnPropertyChanged(nameof(SensitivityLabel));
                ApplySensitivityFilter();
            }
        }
    }

    public string SensitivityLabel => _sensitivityLevel switch
    {
        1 => Loc.Get("SensitivityL1"),
        2 => Loc.Get("SensitivityL2"),
        3 => Loc.Get("SensitivityL3"),
        4 => Loc.Get("SensitivityL4"),
        5 => Loc.Get("SensitivityL5"),
        _ => ""
    };

    // ── FOD overlay regions ──────────────────────────────────
    public ObservableCollection<FodRegion> FodRegions { get; }

    // ── Status summaries ─────────────────────────────────────
    public string XrayFodMode => CurrentInspection?.HasXray == true ? Loc.Get("XrayAvailable") : Loc.Get("FodActive");
    public IReadOnlyList<SystemErrorMessage> SystemErrors => CurrentInspection?.SystemErrors ?? Array.Empty<SystemErrorMessage>();
    public int CriticalErrorCount => SystemErrors.Count(error => error.Severity == SystemErrorSeverity.Critical);
    public int WarningErrorCount => SystemErrors.Count(error => error.Severity == SystemErrorSeverity.Warning);
    public string SystemErrorSummary => SystemErrors.Count == 0
        ? Loc.Get("AllSubsystemsOperational")
        : Loc.Format("CriticalTotalAlerts", CriticalErrorCount, SystemErrors.Count);
    public string FodSummary => CurrentInspection is null || CurrentInspection.FodAlerts.Count == 0
        ? Loc.Get("NoFod")
        : Loc.Format("FodAlerts", Loc.Get("FodSeverity" + CurrentInspection.HighestFodSeverity), CurrentInspection.FodAlerts.Count);
    public string StatusSummary => CurrentInspection?.Status.ToString() ?? "—";

    public Brush InspectionStatusBrush => CurrentInspection?.Status switch
    {
        InspectionStatus.Clear => SuccessBrush,
        InspectionStatus.Review => WarningBrush,
        InspectionStatus.Escalated => DangerBrush,
        _ => OrangeBrush
    };
    public Brush SystemStatusBrush => CriticalErrorCount > 0 ? DangerBrush : WarningErrorCount > 0 ? WarningBrush : SuccessBrush;
    public Brush FodSeverityBrush => CurrentInspection?.HighestFodSeverity switch
    {
        "Critical" => DangerBrush,
        "High" => DangerBrush,
        "Medium" => WarningBrush,
        "Low" => SuccessBrush,
        _ => SuccessBrush
    };

    private static Brush SuccessBrush { get; } = new SolidColorBrush(Color.FromRgb(83, 193, 138));
    private static Brush WarningBrush { get; } = new SolidColorBrush(Color.FromRgb(231, 184, 77));
    private static Brush DangerBrush { get; } = new SolidColorBrush(Color.FromRgb(227, 93, 91));
    private static Brush OrangeBrush { get; } = new SolidColorBrush(Color.FromRgb(240, 147, 60));

    // FOD severity colour brushes (L1=critical → L5=info)
    private static readonly SolidColorBrush L1Fill = new(Color.FromArgb(190, 227, 93, 91));    // deep red
    private static readonly SolidColorBrush L2Fill = new(Color.FromArgb(160, 240, 130, 60));   // orange
    private static readonly SolidColorBrush L3Fill = new(Color.FromArgb(130, 231, 184, 77));   // amber
    private static readonly SolidColorBrush L4Fill = new(Color.FromArgb(100, 83, 193, 138));   // green
    private static readonly SolidColorBrush L5Fill = new(Color.FromArgb(70, 100, 160, 200));   // blue-grey
    private static readonly SolidColorBrush L1Stroke = new(Color.FromRgb(255, 60, 60));
    private static readonly SolidColorBrush L2Stroke = new(Color.FromRgb(255, 160, 60));
    private static readonly SolidColorBrush L3Stroke = new(Color.FromRgb(240, 200, 60));
    private static readonly SolidColorBrush L4Stroke = new(Color.FromRgb(83, 193, 138));
    private static readonly SolidColorBrush L5Stroke = new(Color.FromRgb(100, 160, 200));

    public async Task LoadAsync()
    {
        var inspection = await _inspectionService.GetCurrentInspectionAsync(_session);
        ApplyInspection(inspection);
    }

    public async void ApplyInspection(InspectionRecord inspection)
    {
        CurrentInspection = inspection;
        _suppressStatusSync = true;
        _selectedStatus = inspection.Status;
        OnPropertyChanged(nameof(StatusDisplayText));
        _suppressStatusSync = false;
        OnPropertyChanged(nameof(CurrentUnderVehicleImagePath));
        OnPropertyChanged(nameof(XrayImagePath));
        OnPropertyChanged(nameof(LicensePlateImagePath));
        OnPropertyChanged(nameof(XrayFodMode));
        OnPropertyChanged(nameof(SystemErrors));
        OnPropertyChanged(nameof(CriticalErrorCount));
        OnPropertyChanged(nameof(WarningErrorCount));
        OnPropertyChanged(nameof(SystemErrorSummary));
        OnPropertyChanged(nameof(FodSummary));
        OnPropertyChanged(nameof(StatusSummary));
        OnPropertyChanged(nameof(InspectionStatusBrush));
        OnPropertyChanged(nameof(SystemStatusBrush));
        OnPropertyChanged(nameof(FodSeverityBrush));

        LoadRoiOverlays();
        await LoadPreviousAsync(inspection);
    }

    private void LoadRoiOverlays()
    {
        _allRois.Clear();
        FodRegions.Clear();

        if (!File.Exists(RoiJsonPath))
            return;

        var json = File.ReadAllText(RoiJsonPath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var lanes = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, RoiBox>>>(json, options);
        if (lanes is null) return;

        // Scale ROI coordinates from source image (8192×4096) → canvas (900×460)
        var scaleX = CanvasWidth / ImageWidth;
        var scaleY = CanvasHeight / ImageHeight;

        // Parse severity level number from "L1", "L2", etc.
        foreach (var (severityLevel, classes) in lanes)
        {
            if (!int.TryParse(severityLevel.TrimStart('L'), out var level))
                continue;

            foreach (var (classId, box) in classes)
            {
                _allRois.Add(new RoiEntry
                {
                    Level = level,
                    Label = $"{severityLevel}",
                    X = box.X * scaleX,
                    Y = box.Y * scaleY,
                    W = box.W * scaleX,
                    H = box.H * scaleY,
                    ClassId = classId
                });
            }
        }

        ApplySensitivityFilter();
    }

    private void ApplySensitivityFilter()
    {
        FodRegions.Clear();

        var (fill, stroke) = LevelBrush(_sensitivityLevel);

        foreach (var roi in _allRois)
        {
            if (roi.Level > _sensitivityLevel)
                continue;

            FodRegions.Add(new FodRegion
            {
                X = roi.X,
                Y = roi.Y,
                Width = roi.W,
                Height = roi.H,
                Label = roi.Label,
                SeverityLevel = $"L{roi.Level}",
                FillBrush = LevelToFill(roi.Level),
                StrokeBrush = LevelToStroke(roi.Level)
            });
        }
    }

    private static SolidColorBrush LevelToFill(int level) => level switch
    {
        1 => L1Fill,
        2 => L2Fill,
        3 => L3Fill,
        4 => L4Fill,
        _ => L5Fill
    };

    private static SolidColorBrush LevelToStroke(int level) => level switch
    {
        1 => L1Stroke,
        2 => L2Stroke,
        3 => L3Stroke,
        4 => L4Stroke,
        _ => L5Stroke
    };

    private static (SolidColorBrush fill, SolidColorBrush stroke) LevelBrush(int level) => level switch
    {
        1 => (L1Fill, L1Stroke),
        2 => (L2Fill, L2Stroke),
        3 => (L3Fill, L3Stroke),
        4 => (L4Fill, L4Stroke),
        _ => (L5Fill, L5Stroke)
    };

    private async Task LoadPreviousAsync(InspectionRecord current)
    {
        if (string.IsNullOrWhiteSpace(current.LicensePlate) || current.LicensePlate == "Pending OCR")
        {
            PreviousInspection = null;
            return;
        }

        PreviousInspection = await _inspectionService.GetPreviousByLicensePlateAsync(
            _session, current.LicensePlate, current.TriggerId);
    }

    public async Task UpdateLicensePlateAsync(string oldPlate, string newPlate)
    {
        if (CurrentInspection is null)
            return;

        if (string.IsNullOrWhiteSpace(newPlate))
            return;

        if (string.Equals(oldPlate, newPlate, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            await _inspectionService.UpdateLicensePlateAsync(
                _session, CurrentInspection.Id, oldPlate, newPlate);

            CurrentInspection.LicensePlate = newPlate;
            CurrentInspection.LicensePlateHash = ComputeHashForLocal(newPlate);

            await LoadPreviousAsync(CurrentInspection);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"License plate update failed: {ex.Message}");
        }
    }

    private static string ComputeHashForLocal(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.ToUpperInvariant()));
        return Convert.ToHexString(bytes);
    }

    private sealed class RoiBox
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double W { get; set; }
        public double H { get; set; }
    }

    private sealed class RoiEntry
    {
        public int Level { get; init; }
        public string Label { get; init; } = "";
        public string ClassId { get; init; } = "";
        public double X { get; init; }
        public double Y { get; init; }
        public double W { get; init; }
        public double H { get; init; }
    }
}
