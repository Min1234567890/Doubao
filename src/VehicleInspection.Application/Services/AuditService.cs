using VehicleInspection.Application.Models;
using VehicleInspection.Application.Repositories;

namespace VehicleInspection.Application.Services;

public sealed class AuditService
{
    private readonly IInspectionRepository _repository;

    public AuditService(IInspectionRepository repository)
    {
        _repository = repository;
    }

    public Task RecordAsync(UserSession session, string action, string target, string result, CancellationToken cancellationToken = default)
    {
        var entry = new AuditEntry
        {
            UserName = session.UserName,
            Role = session.Role,
            Action = action,
            Target = target,
            Result = result
        };

        return _repository.AddAuditEntryAsync(entry, cancellationToken);
    }

    public Task<IReadOnlyList<AuditEntry>> GetEntriesAsync(CancellationToken cancellationToken = default)
    {
        return _repository.GetAuditEntriesAsync(cancellationToken);
    }
}
