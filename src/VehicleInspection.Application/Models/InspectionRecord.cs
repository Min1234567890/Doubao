namespace VehicleInspection.Application.Models;

public sealed class InspectionRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string TriggerId { get; init; } = string.Empty;
    public DateTimeOffset ScanTime { get; init; }
    public string LicensePlate { get; set; } = string.Empty;
    public string LicensePlateHash { get; set; } = string.Empty;
    public InspectionStatus Status { get; set; }
    public string UnderVehicleImagePath { get; init; } = string.Empty;
    public string FullVehicleImagePath { get; init; } = string.Empty;
    public string LicensePlateImagePath { get; init; } = string.Empty;
    public string? XrayImagePath { get; init; }
    public IReadOnlyList<FodAlert> FodAlerts { get; init; } = Array.Empty<FodAlert>();
    public string OperatorName { get; init; } = string.Empty;
    public string Lane { get; init; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string SystemHealth { get; init; } = string.Empty;
    public IReadOnlyList<SystemErrorMessage> SystemErrors { get; init; } = Array.Empty<SystemErrorMessage>();

    public bool HasXray => !string.IsNullOrWhiteSpace(XrayImagePath);
    public bool HasFodAlerts => FodAlerts.Count > 0;
    public string HighestFodSeverity => FodAlerts.Count == 0 ? "Clear" : FodAlerts.Select(alert => alert.Severity).Max() ?? "Clear";
}

public enum InspectionStatus
{
    Pending,
    Clear,
    Review,
    Hold,
    Escalated
}

public sealed class FodAlert
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Zone { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public double Confidence { get; init; }
}
