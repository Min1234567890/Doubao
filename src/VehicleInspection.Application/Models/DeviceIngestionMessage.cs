using System.Text.Json.Serialization;

namespace VehicleInspection.Application.Models;

public sealed class DeviceIngestionMessage
{
    public string ApiKey { get; init; } = string.Empty;
    public string TriggerId { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public string DeviceId { get; init; } = string.Empty;
    public string LaneId { get; init; } = string.Empty;
    public string ImageFormat { get; init; } = string.Empty;
    public string ImageBase64 { get; init; } = string.Empty;
    public string? LicensePlate { get; init; }
    public FodPayload? FodJson { get; init; }
}

public sealed class FodPayload
{
    [JsonPropertyName("alerts")]
    public IReadOnlyList<FodAlertPayload> Alerts { get; init; } = Array.Empty<FodAlertPayload>();
}

public sealed class FodAlertPayload
{
    public string Zone { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public double Confidence { get; init; }
}
