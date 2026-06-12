using VehicleInspection.Application.Models;
using VehicleInspection.Application.Repositories;

namespace VehicleInspection.App.Services;

public sealed class HttpInspectionRepository : IInspectionRepository
{
    private readonly BackendInspectionClient _client;
    private readonly List<AuditEntry> _auditEntries = new();

    public HttpInspectionRepository(BackendInspectionClient client)
    {
        _client = client;
    }

    public Task<InspectionRecord> GetCurrentInspectionAsync(CancellationToken cancellationToken = default)
    {
        return _client.GetCurrentInspectionAsync(cancellationToken);
    }

    public Task<InspectionRecord?> GetInspectionByTriggerIdAsync(string triggerId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<InspectionRecord?>(null);
    }

    public Task UpsertInspectionAsync(InspectionRecord inspection, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<InspectionRecord>> SearchAsync(ReportFilter filter, CancellationToken cancellationToken = default)
    {
        return _client.SearchAsync(filter, cancellationToken);
    }

    public Task AddAuditEntryAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        _auditEntries.Add(entry);
        return Task.CompletedTask;
    }

    public Task<InspectionRecord?> GetPreviousByLicensePlateAsync(string licensePlate, string excludeTriggerId, CancellationToken cancellationToken = default)
    {
        return _client.GetPreviousByLicensePlateAsync(licensePlate, excludeTriggerId, cancellationToken);
    }

    public Task UpdateLicensePlateAsync(Guid inspectionId, string licensePlate, string licensePlateHash, CancellationToken cancellationToken = default)
    {
        return _client.UpdateLicensePlateAsync(inspectionId, licensePlate, licensePlateHash, cancellationToken);
    }

    public Task UpdateInspectionStatusAsync(Guid inspectionId, InspectionStatus status, CancellationToken cancellationToken = default)
    {
        return _client.UpdateInspectionStatusAsync(inspectionId, status, cancellationToken);
    }

    public Task UpdateNotesAsync(Guid inspectionId, string notes, CancellationToken cancellationToken = default)
    {
        return _client.UpdateNotesAsync(inspectionId, notes, cancellationToken);
    }

    public Task<IReadOnlyList<AuditEntry>> GetAuditEntriesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<AuditEntry>>(_auditEntries.ToList());
    }
}
