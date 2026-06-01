using VehicleInspection.Application.Models;
using VehicleInspection.Application.Repositories;

namespace VehicleInspection.Application.Services;

public sealed class InspectionService
{
    private readonly IInspectionRepository _repository;
    private readonly AuditService _auditService;

    public InspectionService(IInspectionRepository repository, AuditService auditService)
    {
        _repository = repository;
        _auditService = auditService;
    }

    public async Task<InspectionRecord> GetCurrentInspectionAsync(UserSession session, CancellationToken cancellationToken = default)
    {
        var record = await _repository.GetCurrentInspectionAsync(cancellationToken);
        await _auditService.RecordAsync(session, "ViewDashboard", record.Id.ToString(), "Success", cancellationToken);
        return record;
    }

    public async Task<IReadOnlyList<InspectionRecord>> SearchReportsAsync(UserSession session, ReportFilter filter, CancellationToken cancellationToken = default)
    {
        var records = await _repository.SearchAsync(filter, cancellationToken);
        await _auditService.RecordAsync(session, "SearchReports", $"Count={records.Count}", "Success", cancellationToken);
        return records;
    }
}
