using VehicleInspection.Application.Models;

namespace VehicleInspection.Application.Repositories;

public interface IInspectionRepository
{
    Task<InspectionRecord> GetCurrentInspectionAsync(CancellationToken cancellationToken = default);
    Task<InspectionRecord?> GetInspectionByTriggerIdAsync(string triggerId, CancellationToken cancellationToken = default);
    Task UpsertInspectionAsync(InspectionRecord inspection, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InspectionRecord>> SearchAsync(ReportFilter filter, CancellationToken cancellationToken = default);
    Task AddAuditEntryAsync(AuditEntry entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuditEntry>> GetAuditEntriesAsync(CancellationToken cancellationToken = default);
    Task<InspectionRecord?> GetPreviousByLicensePlateAsync(string licensePlate, string excludeTriggerId, CancellationToken cancellationToken = default);
    Task UpdateLicensePlateAsync(Guid inspectionId, string licensePlate, string licensePlateHash, CancellationToken cancellationToken = default);
    Task UpdateInspectionStatusAsync(Guid inspectionId, InspectionStatus status, CancellationToken cancellationToken = default);
    Task UpdateNotesAsync(Guid inspectionId, string notes, CancellationToken cancellationToken = default);
}
