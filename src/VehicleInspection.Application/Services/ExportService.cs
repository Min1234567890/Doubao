using System.Text;
using VehicleInspection.Application.Models;
using VehicleInspection.Application.Security;

namespace VehicleInspection.Application.Services;

public sealed class ExportService
{
    private readonly AuditService _auditService;
    private readonly AuditedAuthorizationService _authorizationService;

    public ExportService(AuditService auditService, AccessControlService accessControlService)
    {
        _auditService = auditService;
        _authorizationService = new AuditedAuthorizationService(accessControlService, auditService);
    }

    public async Task ExportCsvAsync(UserSession session, IEnumerable<InspectionRecord> records, string path, CancellationToken cancellationToken = default)
    {
        if (!await _authorizationService.AuthorizeAsync(session, Permission.ExportReports, path, cancellationToken))
        {
            await _auditService.RecordAsync(session, "ExportCsv", path, "Denied", cancellationToken);
            throw new UnauthorizedAccessException("The current role is not authorized to export reports.");
        }

        var builder = new StringBuilder();
        builder.AppendLine("ScanTime,LicensePlate,Status,Lane,Operator,FodAlerts,SystemHealth");

        foreach (var record in records)
        {
            var line = string.Join(',',
                Escape(record.ScanTime.LocalDateTime.ToString("s")),
                Escape(record.LicensePlate),
                Escape(record.Status.ToString()),
                Escape(record.Lane),
                Escape(record.OperatorName),
                Escape(record.FodAlerts.Count.ToString()),
                Escape(record.SystemHealth));
            builder.AppendLine(line);
        }

        await File.WriteAllTextAsync(path, builder.ToString(), Encoding.UTF8, cancellationToken);
        await _auditService.RecordAsync(session, "ExportCsv", path, "Success", cancellationToken);
    }

    public async Task ExportPdfManifestAsync(UserSession session, IEnumerable<InspectionRecord> records, string path, CancellationToken cancellationToken = default)
    {
        if (!await _authorizationService.AuthorizeAsync(session, Permission.ExportReports, path, cancellationToken))
        {
            await _auditService.RecordAsync(session, "ExportPdf", path, "Denied", cancellationToken);
            throw new UnauthorizedAccessException("The current role is not authorized to export reports.");
        }

        var builder = new StringBuilder();
        builder.AppendLine("UVSS Secure Inspection Report");
        builder.AppendLine("Generated: " + DateTimeOffset.Now.ToString("u"));
        builder.AppendLine("Records: " + records.Count());
        builder.AppendLine("This baseline writes a PDF-ready manifest. Use an enterprise-approved signed PDF library in production.");
        await File.WriteAllTextAsync(path, builder.ToString(), Encoding.UTF8, cancellationToken);
        await _auditService.RecordAsync(session, "ExportPdf", path, "Success", cancellationToken);
    }

    private static string Escape(string value)
    {
        return value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? '"' + value.Replace("\"", "\"\"") + '"'
            : value;
    }
}
