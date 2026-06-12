using VehicleInspection.Application.Models;
using VehicleInspection.Application.Repositories;

namespace VehicleInspection.Data.Repositories;

public sealed class InMemoryInspectionRepository : IInspectionRepository
{
    private readonly List<InspectionRecord> _inspections = new();
    private readonly List<AuditEntry> _auditEntries = new();

    public InMemoryInspectionRepository()
    {
        _inspections.AddRange(CreateSeedRecords());
    }

    public Task<InspectionRecord> GetCurrentInspectionAsync(CancellationToken cancellationToken = default)
    {
        lock (_inspections)
        {
            return Task.FromResult(_inspections.OrderByDescending(record => record.ScanTime).First());
        }
    }

    public Task<InspectionRecord?> GetInspectionByTriggerIdAsync(string triggerId, CancellationToken cancellationToken = default)
    {
        lock (_inspections)
        {
            return Task.FromResult(_inspections.FirstOrDefault(record => record.TriggerId.Equals(triggerId, StringComparison.OrdinalIgnoreCase)));
        }
    }

    public Task UpsertInspectionAsync(InspectionRecord inspection, CancellationToken cancellationToken = default)
    {
        lock (_inspections)
        {
            var index = _inspections.FindIndex(record => record.Id == inspection.Id || (!string.IsNullOrWhiteSpace(record.TriggerId) && record.TriggerId.Equals(inspection.TriggerId, StringComparison.OrdinalIgnoreCase)));
            if (index >= 0)
            {
                _inspections[index] = inspection;
            }
            else
            {
                _inspections.Add(inspection);
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<InspectionRecord>> SearchAsync(ReportFilter filter, CancellationToken cancellationToken = default)
    {
        IEnumerable<InspectionRecord> query;
        lock (_inspections)
        {
            query = _inspections.ToList();
        }

        if (filter.FromDate.HasValue)
        {
            query = query.Where(record => record.ScanTime.LocalDateTime.Date >= filter.FromDate.Value.Date);
        }

        if (filter.ToDate.HasValue)
        {
            query = query.Where(record => record.ScanTime.LocalDateTime.Date <= filter.ToDate.Value.Date);
        }

        if (!string.IsNullOrWhiteSpace(filter.LicensePlate))
        {
            query = query.Where(record => record.LicensePlate.Contains(filter.LicensePlate, StringComparison.OrdinalIgnoreCase));
        }

        if (filter.Status.HasValue)
        {
            query = query.Where(record => record.Status == filter.Status.Value);
        }

        if (filter.FodAlertsOnly)
        {
            query = query.Where(record => record.HasFodAlerts);
        }

        return Task.FromResult<IReadOnlyList<InspectionRecord>>(query.OrderByDescending(record => record.ScanTime).ToList());
    }

    public Task AddAuditEntryAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        _auditEntries.Add(entry);
        return Task.CompletedTask;
    }

    public Task<InspectionRecord?> GetPreviousByLicensePlateAsync(string licensePlate, string excludeTriggerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(licensePlate))
            return Task.FromResult<InspectionRecord?>(null);

        lock (_inspections)
        {
            var previous = _inspections
                .Where(r => r.LicensePlate.Equals(licensePlate, StringComparison.OrdinalIgnoreCase)
                            && !r.TriggerId.Equals(excludeTriggerId, StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrWhiteSpace(r.UnderVehicleImagePath))
                .OrderByDescending(r => r.ScanTime)
                .FirstOrDefault();
            return Task.FromResult<InspectionRecord?>(previous);
        }
    }

    public Task UpdateLicensePlateAsync(Guid inspectionId, string licensePlate, string licensePlateHash, CancellationToken cancellationToken = default)
    {
        lock (_inspections)
        {
            var index = _inspections.FindIndex(record => record.Id == inspectionId);
            if (index >= 0)
            {
                _inspections[index].LicensePlate = licensePlate;
                _inspections[index].LicensePlateHash = licensePlateHash;
            }
        }

        return Task.CompletedTask;
    }

    public Task UpdateInspectionStatusAsync(Guid inspectionId, InspectionStatus status, CancellationToken cancellationToken = default)
    {
        lock (_inspections)
        {
            var index = _inspections.FindIndex(record => record.Id == inspectionId);
            if (index >= 0)
            {
                _inspections[index].Status = status;
            }
        }

        return Task.CompletedTask;
    }

    public Task UpdateNotesAsync(Guid inspectionId, string notes, CancellationToken cancellationToken = default)
    {
        lock (_inspections)
        {
            var index = _inspections.FindIndex(record => record.Id == inspectionId);
            if (index >= 0) _inspections[index].Notes = notes;
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditEntry>> GetAuditEntriesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<AuditEntry>>(_auditEntries.OrderByDescending(entry => entry.EventTimeUtc).ToList());
    }

    private static IEnumerable<InspectionRecord> CreateSeedRecords()
    {
        var now = DateTimeOffset.Now;
        return new[]
        {
            new InspectionRecord
            {
                TriggerId = "SEED-SEC-2048",
                ScanTime = now.AddMinutes(-4),
                LicensePlate = "SEC-2048",
                LicensePlateHash = "B73A0A7B6E8E4E3C",
                Status = InspectionStatus.Review,
                UnderVehicleImagePath = string.Empty,
                FullVehicleImagePath = string.Empty,
                LicensePlateImagePath = string.Empty,
                XrayImagePath = null,
                FodAlerts = new[]
                {
                    new FodAlert { Zone = "Rear axle", Severity = "High", Description = "Foreign object detected near exhaust line", Confidence = 0.94 },
                    new FodAlert { Zone = "Center channel", Severity = "Medium", Description = "Unmatched undercarriage contour", Confidence = 0.82 }
                },
                OperatorName = "Operator Chen",
                Lane = "Gate A / Lane 02",
                Notes = "Vehicle held for secondary inspection. Awaiting supervisor review.",
                SystemHealth = "Critical subsystem alerts detected",
                SystemErrors = new[]
                {
                    new SystemErrorMessage { Subsystem = SubsystemName.Uvss, Severity = SystemErrorSeverity.Critical, Message = "UVSS scanner is not responding", OperatorAction = "Hold lane and switch to manual undervehicle inspection." },
                    new SystemErrorMessage { Subsystem = SubsystemName.Database, Severity = SystemErrorSeverity.Critical, Message = "Database connection is down", OperatorAction = "Continue local review; exports and history may be unavailable." },
                    new SystemErrorMessage { Subsystem = SubsystemName.RestApi, Severity = SystemErrorSeverity.Warning, Message = "REST API heartbeat timeout", OperatorAction = "Verify application service health and network route." },
                    new SystemErrorMessage { Subsystem = SubsystemName.Vlpr, Severity = SystemErrorSeverity.Critical, Message = "VLPR system is down", OperatorAction = "Manually verify plate image and record plate number." }
                }
            },
            new InspectionRecord
            {
                TriggerId = "SEED-UVS-1186",
                ScanTime = now.AddMinutes(-18),
                LicensePlate = "UVS-1186",
                LicensePlateHash = "1D9CF3110F77BB92",
                Status = InspectionStatus.Clear,
                UnderVehicleImagePath = string.Empty,
                FullVehicleImagePath = string.Empty,
                LicensePlateImagePath = string.Empty,
                XrayImagePath = "sample-xray.png",
                FodAlerts = Array.Empty<FodAlert>(),
                OperatorName = "Operator Liu",
                Lane = "Gate A / Lane 01",
                Notes = "Cleared after X-ray comparison.",
                SystemHealth = "All sensors online"
            },
            new InspectionRecord
            {
                TriggerId = "SEED-GOV-7605",
                ScanTime = now.AddHours(-2),
                LicensePlate = "GOV-7605",
                LicensePlateHash = "08F1E5CF4B7A142A",
                Status = InspectionStatus.Escalated,
                UnderVehicleImagePath = string.Empty,
                FullVehicleImagePath = string.Empty,
                LicensePlateImagePath = string.Empty,
                XrayImagePath = null,
                FodAlerts = new[]
                {
                    new FodAlert { Zone = "Front right", Severity = "Critical", Description = "Object profile differs from baseline", Confidence = 0.97 }
                },
                OperatorName = "Operator Smith",
                Lane = "Embassy Gate / Lane 03",
                Notes = "Escalated to site security commander.",
                SystemHealth = "Camera 3 requires cleaning"
            }
        };
    }
}
