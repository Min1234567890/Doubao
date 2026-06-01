using VehicleInspection.Application.Security;

namespace VehicleInspection.Application.Models;

public sealed class UserSession
{
    public string UserName { get; init; } = Environment.UserName;
    public Role Role { get; init; } = Role.Operator;
    public string AuthenticationProvider { get; init; } = "LocalWindows";
    public string IdentityName { get; init; } = Environment.UserName;
    public IReadOnlyList<string> SecurityGroups { get; init; } = Array.Empty<string>();
    public string AuthenticationDetail { get; init; } = string.Empty;
    public DateTimeOffset LoginTime { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastActivityUtc { get; private set; } = DateTimeOffset.UtcNow;
    public bool IsLocked { get; private set; }

    public void Touch()
    {
        LastActivityUtc = DateTimeOffset.UtcNow;
    }

    public void Lock()
    {
        IsLocked = true;
    }

    public void Unlock()
    {
        IsLocked = false;
        Touch();
    }
}
