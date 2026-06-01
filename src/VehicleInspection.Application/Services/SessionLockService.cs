using VehicleInspection.Application.Models;

namespace VehicleInspection.Application.Services;

public sealed class SessionLockService
{
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(15);

    public bool ShouldLock(UserSession session, DateTimeOffset nowUtc)
    {
        return !session.IsLocked && nowUtc - session.LastActivityUtc >= IdleTimeout;
    }
}
