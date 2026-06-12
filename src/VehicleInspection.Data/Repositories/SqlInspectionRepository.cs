using System.Text.Json;
using Microsoft.Data.SqlClient;
using VehicleInspection.Application.Models;
using VehicleInspection.Application.Repositories;
using VehicleInspection.Application.Security;

namespace VehicleInspection.Data.Repositories;

public sealed class SqlInspectionRepository : IInspectionRepository
{
    private readonly string _connectionString;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public SqlInspectionRepository(string connectionString)
    {
        _connectionString = connectionString;
        EnsureSchemaAsync().GetAwaiter().GetResult();
    }

    private async Task EnsureSchemaAsync()
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        const string createInspections = @"
            IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Inspections')
            BEGIN
                CREATE TABLE Inspections (
                    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    TriggerId NVARCHAR(96) NOT NULL,
                    ScanTime DATETIMEOFFSET NOT NULL,
                    LicensePlate NVARCHAR(50) NOT NULL,
                    LicensePlateHash NVARCHAR(64) NOT NULL,
                    Status INT NOT NULL,
                    UnderVehicleImagePath NVARCHAR(500) NOT NULL,
                    FullVehicleImagePath NVARCHAR(500) NOT NULL,
                    LicensePlateImagePath NVARCHAR(500) NOT NULL,
                    XrayImagePath NVARCHAR(500) NULL,
                    FodAlertsJson NVARCHAR(MAX) NOT NULL,
                    OperatorName NVARCHAR(200) NOT NULL,
                    Lane NVARCHAR(100) NOT NULL,
                    Notes NVARCHAR(MAX) NOT NULL,
                    SystemHealth NVARCHAR(500) NOT NULL,
                    SystemErrorsJson NVARCHAR(MAX) NOT NULL
                );
                CREATE INDEX IX_Inspections_ScanTime ON Inspections(ScanTime DESC);
                CREATE INDEX IX_Inspections_TriggerId ON Inspections(TriggerId);
                CREATE INDEX IX_Inspections_Status ON Inspections(Status);
            END";

        const string createAudit = @"
            IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'AuditEntries')
            BEGIN
                CREATE TABLE AuditEntries (
                    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    EventTimeUtc DATETIMEOFFSET NOT NULL,
                    UserName NVARCHAR(200) NOT NULL,
                    Role INT NOT NULL,
                    Action NVARCHAR(200) NOT NULL,
                    Target NVARCHAR(500) NOT NULL,
                    Result NVARCHAR(200) NOT NULL,
                    Workstation NVARCHAR(200) NOT NULL
                );
                CREATE INDEX IX_AuditEntries_EventTimeUtc ON AuditEntries(EventTimeUtc DESC);
            END";

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = createInspections;
        await cmd.ExecuteNonQueryAsync();

        cmd.CommandText = createAudit;
        await cmd.ExecuteNonQueryAsync();

        // Seed data if table is empty
        cmd.CommandText = "SELECT COUNT(1) FROM Inspections";
        var count = (int)(await cmd.ExecuteScalarAsync() ?? 0);
        if (count == 0)
        {
            foreach (var record in CreateSeedRecords())
            {
                await InsertInspectionAsync(connection, record);
            }
        }
    }

    public async Task<InspectionRecord> GetCurrentInspectionAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT TOP 1 * FROM Inspections ORDER BY ScanTime DESC";

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return ReadInspectionRecord(reader);
        }

        throw new InvalidOperationException("No inspection records found.");
    }

    public async Task<InspectionRecord?> GetInspectionByTriggerIdAsync(string triggerId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT TOP 1 * FROM Inspections WHERE TriggerId = @TriggerId";
        cmd.Parameters.AddWithValue("@TriggerId", triggerId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return ReadInspectionRecord(reader);
        }

        return null;
    }

    public async Task UpsertInspectionAsync(InspectionRecord inspection, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(1) FROM Inspections WHERE Id = @Id OR TriggerId = @TriggerId";
        checkCmd.Parameters.AddWithValue("@Id", inspection.Id);
        checkCmd.Parameters.AddWithValue("@TriggerId", inspection.TriggerId);
        var exists = (int)await checkCmd.ExecuteScalarAsync(cancellationToken) > 0;

        if (exists)
        {
            await UpdateInspectionAsync(connection, inspection, cancellationToken);
        }
        else
        {
            await InsertInspectionAsync(connection, inspection, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<InspectionRecord>> SearchAsync(ReportFilter filter, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var where = new List<string>();
        var parameters = new List<SqlParameter>();

        if (filter.FromDate.HasValue)
        {
            where.Add("ScanTime >= @FromDate");
            parameters.Add(new SqlParameter("@FromDate", filter.FromDate.Value));
        }

        if (filter.ToDate.HasValue)
        {
            // Include the full end date
            var toDateEnd = filter.ToDate.Value.Date.AddDays(1).AddTicks(-1);
            where.Add("ScanTime <= @ToDate");
            parameters.Add(new SqlParameter("@ToDate", toDateEnd));
        }

        if (!string.IsNullOrWhiteSpace(filter.LicensePlate))
        {
            where.Add("LicensePlate LIKE @LicensePlate");
            parameters.Add(new SqlParameter("@LicensePlate", $"%{filter.LicensePlate}%"));
        }

        if (filter.Status.HasValue)
        {
            where.Add("Status = @Status");
            parameters.Add(new SqlParameter("@Status", (int)filter.Status.Value));
        }

        if (filter.FodAlertsOnly)
        {
            where.Add("FodAlertsJson != '[]'");
        }

        var whereClause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";
        var sql = $"SELECT * FROM Inspections {whereClause} ORDER BY ScanTime DESC";

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddRange(parameters.ToArray());

        var results = new List<InspectionRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadInspectionRecord(reader));
        }

        return results;
    }

    public async Task AddAuditEntryAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO AuditEntries (Id, EventTimeUtc, UserName, Role, Action, Target, Result, Workstation)
            VALUES (@Id, @EventTimeUtc, @UserName, @Role, @Action, @Target, @Result, @Workstation)";

        cmd.Parameters.AddWithValue("@Id", entry.Id);
        cmd.Parameters.AddWithValue("@EventTimeUtc", entry.EventTimeUtc);
        cmd.Parameters.AddWithValue("@UserName", entry.UserName);
        cmd.Parameters.AddWithValue("@Role", (int)entry.Role);
        cmd.Parameters.AddWithValue("@Action", entry.Action);
        cmd.Parameters.AddWithValue("@Target", entry.Target);
        cmd.Parameters.AddWithValue("@Result", entry.Result);
        cmd.Parameters.AddWithValue("@Workstation", entry.Workstation);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<InspectionRecord?> GetPreviousByLicensePlateAsync(string licensePlate, string excludeTriggerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(licensePlate))
            return null;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT TOP 1 * FROM Inspections
            WHERE LicensePlate = @LicensePlate
              AND TriggerId != @ExcludeTriggerId
              AND UnderVehicleImagePath != ''
            ORDER BY ScanTime DESC";
        cmd.Parameters.AddWithValue("@LicensePlate", licensePlate);
        cmd.Parameters.AddWithValue("@ExcludeTriggerId", excludeTriggerId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return ReadInspectionRecord(reader);
        }

        return null;
    }

    public async Task UpdateLicensePlateAsync(Guid inspectionId, string licensePlate, string licensePlateHash, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            UPDATE Inspections SET
                LicensePlate = @LicensePlate,
                LicensePlateHash = @LicensePlateHash
            WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", inspectionId);
        cmd.Parameters.AddWithValue("@LicensePlate", licensePlate);
        cmd.Parameters.AddWithValue("@LicensePlateHash", licensePlateHash);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateInspectionStatusAsync(Guid inspectionId, InspectionStatus status, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE Inspections SET Status = @Status WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", inspectionId);
        cmd.Parameters.AddWithValue("@Status", (int)status);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateNotesAsync(Guid inspectionId, string notes, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE Inspections SET Notes = @Notes WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", inspectionId);
        cmd.Parameters.AddWithValue("@Notes", notes);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuditEntry>> GetAuditEntriesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM AuditEntries ORDER BY EventTimeUtc DESC";

        var results = new List<AuditEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new AuditEntry
            {
                Id = reader.GetGuid(0),
                EventTimeUtc = reader.GetDateTimeOffset(1),
                UserName = reader.GetString(2),
                Role = (Role)reader.GetInt32(3),
                Action = reader.GetString(4),
                Target = reader.GetString(5),
                Result = reader.GetString(6),
                Workstation = reader.GetString(7)
            });
        }

        return results;
    }

    private static async Task InsertInspectionAsync(SqlConnection connection, InspectionRecord inspection, CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Inspections (Id, TriggerId, ScanTime, LicensePlate, LicensePlateHash, Status,
                UnderVehicleImagePath, FullVehicleImagePath, LicensePlateImagePath, XrayImagePath,
                FodAlertsJson, OperatorName, Lane, Notes, SystemHealth, SystemErrorsJson)
            VALUES (@Id, @TriggerId, @ScanTime, @LicensePlate, @LicensePlateHash, @Status,
                @UnderVehicleImagePath, @FullVehicleImagePath, @LicensePlateImagePath, @XrayImagePath,
                @FodAlertsJson, @OperatorName, @Lane, @Notes, @SystemHealth, @SystemErrorsJson)";

        cmd.Parameters.AddWithValue("@Id", inspection.Id);
        cmd.Parameters.AddWithValue("@TriggerId", inspection.TriggerId);
        cmd.Parameters.AddWithValue("@ScanTime", inspection.ScanTime);
        cmd.Parameters.AddWithValue("@LicensePlate", inspection.LicensePlate);
        cmd.Parameters.AddWithValue("@LicensePlateHash", inspection.LicensePlateHash);
        cmd.Parameters.AddWithValue("@Status", (int)inspection.Status);
        cmd.Parameters.AddWithValue("@UnderVehicleImagePath", inspection.UnderVehicleImagePath);
        cmd.Parameters.AddWithValue("@FullVehicleImagePath", inspection.FullVehicleImagePath);
        cmd.Parameters.AddWithValue("@LicensePlateImagePath", inspection.LicensePlateImagePath);
        cmd.Parameters.AddWithValue("@XrayImagePath", (object?)inspection.XrayImagePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@FodAlertsJson", SerializeFodAlerts(inspection.FodAlerts));
        cmd.Parameters.AddWithValue("@OperatorName", inspection.OperatorName);
        cmd.Parameters.AddWithValue("@Lane", inspection.Lane);
        cmd.Parameters.AddWithValue("@Notes", inspection.Notes);
        cmd.Parameters.AddWithValue("@SystemHealth", inspection.SystemHealth);
        cmd.Parameters.AddWithValue("@SystemErrorsJson", SerializeSystemErrors(inspection.SystemErrors));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpdateInspectionAsync(SqlConnection connection, InspectionRecord inspection, CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            UPDATE Inspections SET
                TriggerId = @TriggerId,
                ScanTime = @ScanTime,
                LicensePlate = @LicensePlate,
                LicensePlateHash = @LicensePlateHash,
                Status = @Status,
                UnderVehicleImagePath = @UnderVehicleImagePath,
                FullVehicleImagePath = @FullVehicleImagePath,
                LicensePlateImagePath = @LicensePlateImagePath,
                XrayImagePath = @XrayImagePath,
                FodAlertsJson = @FodAlertsJson,
                OperatorName = @OperatorName,
                Lane = @Lane,
                Notes = @Notes,
                SystemHealth = @SystemHealth,
                SystemErrorsJson = @SystemErrorsJson
            WHERE Id = @Id OR TriggerId = @TriggerId";

        cmd.Parameters.AddWithValue("@Id", inspection.Id);
        cmd.Parameters.AddWithValue("@TriggerId", inspection.TriggerId);
        cmd.Parameters.AddWithValue("@ScanTime", inspection.ScanTime);
        cmd.Parameters.AddWithValue("@LicensePlate", inspection.LicensePlate);
        cmd.Parameters.AddWithValue("@LicensePlateHash", inspection.LicensePlateHash);
        cmd.Parameters.AddWithValue("@Status", (int)inspection.Status);
        cmd.Parameters.AddWithValue("@UnderVehicleImagePath", inspection.UnderVehicleImagePath);
        cmd.Parameters.AddWithValue("@FullVehicleImagePath", inspection.FullVehicleImagePath);
        cmd.Parameters.AddWithValue("@LicensePlateImagePath", inspection.LicensePlateImagePath);
        cmd.Parameters.AddWithValue("@XrayImagePath", (object?)inspection.XrayImagePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@FodAlertsJson", SerializeFodAlerts(inspection.FodAlerts));
        cmd.Parameters.AddWithValue("@OperatorName", inspection.OperatorName);
        cmd.Parameters.AddWithValue("@Lane", inspection.Lane);
        cmd.Parameters.AddWithValue("@Notes", inspection.Notes);
        cmd.Parameters.AddWithValue("@SystemHealth", inspection.SystemHealth);
        cmd.Parameters.AddWithValue("@SystemErrorsJson", SerializeSystemErrors(inspection.SystemErrors));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static InspectionRecord ReadInspectionRecord(SqlDataReader reader)
    {
        return new InspectionRecord
        {
            Id = reader.GetGuid(0),
            TriggerId = reader.GetString(1),
            ScanTime = reader.GetDateTimeOffset(2),
            LicensePlate = reader.GetString(3),
            LicensePlateHash = reader.GetString(4),
            Status = (InspectionStatus)reader.GetInt32(5),
            UnderVehicleImagePath = reader.GetString(6),
            FullVehicleImagePath = reader.GetString(7),
            LicensePlateImagePath = reader.GetString(8),
            XrayImagePath = reader.IsDBNull(9) ? null : reader.GetString(9),
            FodAlerts = DeserializeFodAlerts(reader.GetString(10)),
            OperatorName = reader.GetString(11),
            Lane = reader.GetString(12),
            Notes = reader.GetString(13),
            SystemHealth = reader.GetString(14),
            SystemErrors = DeserializeSystemErrors(reader.GetString(15))
        };
    }

    private static string SerializeFodAlerts(IReadOnlyList<FodAlert> alerts)
    {
        return JsonSerializer.Serialize(alerts, JsonOptions);
    }

    private static IReadOnlyList<FodAlert> DeserializeFodAlerts(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]")
            return Array.Empty<FodAlert>();

        var result = JsonSerializer.Deserialize<List<FodAlert>>(json, JsonOptions);
        return result is not null && result.Count > 0 ? result : Array.Empty<FodAlert>();
    }

    private static string SerializeSystemErrors(IReadOnlyList<SystemErrorMessage> errors)
    {
        return JsonSerializer.Serialize(errors, JsonOptions);
    }

    private static IReadOnlyList<SystemErrorMessage> DeserializeSystemErrors(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]")
            return Array.Empty<SystemErrorMessage>();

        var result = JsonSerializer.Deserialize<List<SystemErrorMessage>>(json, JsonOptions);
        return result is not null && result.Count > 0 ? result : Array.Empty<SystemErrorMessage>();
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
