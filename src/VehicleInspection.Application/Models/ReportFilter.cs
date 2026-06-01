namespace VehicleInspection.Application.Models;

public sealed class ReportFilter
{
    public DateTime? FromDate { get; init; }
    public DateTime? ToDate { get; init; }
    public string LicensePlate { get; init; } = string.Empty;
    public InspectionStatus? Status { get; init; }
    public bool FodAlertsOnly { get; init; }
}
