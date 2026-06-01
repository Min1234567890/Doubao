using VehicleInspection.Application.Models;
using VehicleInspection.Application.Services;

namespace VehicleInspection.Application.Security;

public sealed class AuditedAuthorizationService
{
    private readonly AccessControlService _accessControlService;
    private readonly AuditService _auditService;

    public AuditedAuthorizationService(AccessControlService accessControlService, AuditService auditService)
    {
        _accessControlService = accessControlService;
        _auditService = auditService;
    }

    public async Task<bool> AuthorizeAsync(UserSession session, Permission permission, string target, CancellationToken cancellationToken = default)
    {
        var authorized = _accessControlService.Can(session.Role, permission);
        await _auditService.RecordAsync(session, authorized ? "AccessGranted" : "AccessDenied", target, permission.ToString(), cancellationToken);
        return authorized;
    }
}
