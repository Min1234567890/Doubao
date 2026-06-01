using VehicleInspection.Application.Security;

namespace VehicleInspection.Application.Models;

public sealed class AuditEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset EventTimeUtc { get; init; } = DateTimeOffset.UtcNow;
    public string UserName { get; init; } = string.Empty;
    public Role Role { get; init; }
    public string Action { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string Result { get; init; } = string.Empty;
    public string Workstation { get; init; } = Environment.MachineName;
}
