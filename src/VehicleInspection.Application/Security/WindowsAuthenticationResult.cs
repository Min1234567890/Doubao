using VehicleInspection.Application.Models;

namespace VehicleInspection.Application.Security;

public sealed class WindowsAuthenticationResult
{
    public required UserSession Session { get; init; }
    public required WindowsAuthenticationMode Mode { get; init; }
    public required string IdentityName { get; init; }
    public required IReadOnlyList<string> MatchedGroups { get; init; }
    public required string ResultDetail { get; init; }
}
