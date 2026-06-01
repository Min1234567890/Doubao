namespace VehicleInspection.Application.Models;

public sealed class SystemErrorMessage
{
    public SubsystemName Subsystem { get; init; }
    public SystemErrorSeverity Severity { get; init; }
    public string Message { get; init; } = string.Empty;
    public DateTimeOffset LastSeenUtc { get; init; } = DateTimeOffset.UtcNow;
    public string OperatorAction { get; init; } = string.Empty;

    public string DisplayName => Subsystem switch
    {
        SubsystemName.Uvss => "UVSS",
        SubsystemName.Database => "Database",
        SubsystemName.RestApi => "REST API",
        SubsystemName.Vlpr => "VLPR",
        _ => Subsystem.ToString()
    };
}

public enum SubsystemName
{
    Uvss,
    Database,
    RestApi,
    Vlpr
}

public enum SystemErrorSeverity
{
    Info,
    Warning,
    Critical
}
