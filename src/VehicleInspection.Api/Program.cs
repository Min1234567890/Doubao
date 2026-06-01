using VehicleInspection.Application.Models;
using VehicleInspection.Application.Repositories;
using VehicleInspection.Application.Services;
using VehicleInspection.Data.Repositories;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["UVSS_BACKEND_URL"] ?? "http://localhost:5077");

builder.Services.AddSingleton<IInspectionRepository, InMemoryInspectionRepository>();
builder.Services.AddSingleton<AuditService>();
builder.Services.AddSingleton(provider =>
{
    var repository = provider.GetRequiredService<IInspectionRepository>();
    var imageRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UVSS", "VehicleInspection", "BackendImages");
    return new DeviceIngestionService(repository, builder.Configuration["UVSS_DEVICE_API_KEY"] ?? "development-key-change-me", imageRoot, "http://localhost:5077/api/images");
});
builder.Services.AddSingleton<InspectionService>();

var app = builder.Build();

app.MapPost("/api/device-ingestion", async (DeviceIngestionMessage message, DeviceIngestionService ingestionService, CancellationToken cancellationToken) =>
{
    var record = await ingestionService.ProcessAsync(message, cancellationToken);
    return record == null ? Results.Accepted(value: new { status = "DuplicateIgnored" }) : Results.Ok(record);
});

app.MapGet("/api/inspections/current", async (IInspectionRepository repository, CancellationToken cancellationToken) =>
{
    return Results.Ok(await repository.GetCurrentInspectionAsync(cancellationToken));
});

app.MapGet("/api/inspections", async (DateTime? fromDate, DateTime? toDate, string? licensePlate, InspectionStatus? status, bool? fodAlertsOnly, IInspectionRepository repository, CancellationToken cancellationToken) =>
{
    var filter = new ReportFilter
    {
        FromDate = fromDate,
        ToDate = toDate,
        LicensePlate = licensePlate ?? string.Empty,
        Status = status,
        FodAlertsOnly = fodAlertsOnly ?? false
    };

    return Results.Ok(await repository.SearchAsync(filter, cancellationToken));
});

app.MapGet("/api/images/{triggerId}/{fileName}", (string triggerId, string fileName) =>
{
    var imageRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UVSS", "VehicleInspection", "BackendImages");
    var triggerDirectory = Path.Combine(imageRoot, SafeSegment(triggerId));
    var path = Path.Combine(triggerDirectory, Path.GetFileName(fileName));

    return File.Exists(path) ? Results.File(path) : Results.NotFound();
});

app.Run();

static string SafeSegment(string value)
{
    return string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));
}
