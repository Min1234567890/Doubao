using System.Security.Cryptography;
using System.Text;
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

    public async Task<InspectionRecord?> GetPreviousByLicensePlateAsync(UserSession session, string licensePlate, string excludeTriggerId, CancellationToken cancellationToken = default)
    {
        var record = await _repository.GetPreviousByLicensePlateAsync(licensePlate, excludeTriggerId, cancellationToken);
        if (record is not null)
        {
            await _auditService.RecordAsync(session, "ViewPreviousScan", record.Id.ToString(), "Success", cancellationToken);
        }
        return record;
    }

    public async Task UpdateLicensePlateAsync(UserSession session, Guid inspectionId, string oldLicensePlate, string newLicensePlate, CancellationToken cancellationToken = default)
    {
        var hash = ComputeLicensePlateHash(newLicensePlate);
        await _repository.UpdateLicensePlateAsync(inspectionId, newLicensePlate, hash, cancellationToken);
        await _auditService.RecordAsync(session, "UpdateLicensePlate", inspectionId.ToString(),
            $"Success: {oldLicensePlate} -> {newLicensePlate}", cancellationToken);
    }

    public async Task UpdateInspectionStatusAsync(UserSession session, Guid inspectionId, InspectionStatus oldStatus, InspectionStatus newStatus, CancellationToken cancellationToken = default)
    {
        await _repository.UpdateInspectionStatusAsync(inspectionId, newStatus, cancellationToken);
        await _auditService.RecordAsync(session, "UpdateStatus", inspectionId.ToString(),
            $"Success: {oldStatus} -> {newStatus}", cancellationToken);
    }

    public async Task UpdateNotesAsync(UserSession session, Guid inspectionId, string notes, CancellationToken cancellationToken = default)
    {
        await _repository.UpdateNotesAsync(inspectionId, notes, cancellationToken);
        await _auditService.RecordAsync(session, "UpdateNotes", inspectionId.ToString(), "Success", cancellationToken);
    }

    private static string ComputeLicensePlateHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.ToUpperInvariant()));
        return Convert.ToHexString(bytes);
    }
}
