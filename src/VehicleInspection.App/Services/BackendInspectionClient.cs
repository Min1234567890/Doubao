using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using VehicleInspection.Application.Models;

namespace VehicleInspection.App.Services;

public sealed class BackendInspectionClient
{
    private readonly HttpClient _httpClient;

    public BackendInspectionClient(string baseUrl)
    {
        _httpClient = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
    }

    public async Task<InspectionRecord?> ForwardDeviceMessageAsync(DeviceIngestionMessage message, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("api/device-ingestion", message, cancellationToken);
        response.EnsureSuccessStatusCode();

        return response.StatusCode == System.Net.HttpStatusCode.Accepted
            ? null
            : await response.Content.ReadFromJsonAsync<InspectionRecord>(cancellationToken: cancellationToken);
    }

    public async Task<InspectionRecord> GetCurrentInspectionAsync(CancellationToken cancellationToken = default)
    {
        return await _httpClient.GetFromJsonAsync<InspectionRecord>("api/inspections/current", cancellationToken)
            ?? throw new InvalidDataException("Backend returned an empty current inspection response.");
    }

    public async Task<IReadOnlyList<InspectionRecord>> SearchAsync(ReportFilter filter, CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        if (filter.FromDate.HasValue)
        {
            query.Add($"fromDate={Uri.EscapeDataString(filter.FromDate.Value.ToString("O"))}");
        }

        if (filter.ToDate.HasValue)
        {
            query.Add($"toDate={Uri.EscapeDataString(filter.ToDate.Value.ToString("O"))}");
        }

        if (!string.IsNullOrWhiteSpace(filter.LicensePlate))
        {
            query.Add($"licensePlate={Uri.EscapeDataString(filter.LicensePlate)}");
        }

        if (filter.Status.HasValue)
        {
            query.Add($"status={Uri.EscapeDataString(filter.Status.Value.ToString())}");
        }

        if (filter.FodAlertsOnly)
        {
            query.Add("fodAlertsOnly=true");
        }

        var path = query.Count == 0 ? "api/inspections" : $"api/inspections?{string.Join('&', query)}";
        return await _httpClient.GetFromJsonAsync<IReadOnlyList<InspectionRecord>>(path, cancellationToken) ?? Array.Empty<InspectionRecord>();
    }

    public async Task<InspectionRecord?> GetPreviousByLicensePlateAsync(string licensePlate, string excludeTriggerId, CancellationToken cancellationToken = default)
    {
        var path = $"api/inspections/previous?licensePlate={Uri.EscapeDataString(licensePlate)}&excludeTriggerId={Uri.EscapeDataString(excludeTriggerId)}";
        var response = await _httpClient.GetAsync(path, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<InspectionRecord>(cancellationToken: cancellationToken);
    }

    public async Task UpdateLicensePlateAsync(Guid inspectionId, string licensePlate, string licensePlateHash, CancellationToken cancellationToken = default)
    {
        var payload = new { licensePlate, licensePlateHash };
        var response = await _httpClient.PutAsJsonAsync($"api/inspections/{inspectionId}/license-plate", payload, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task UpdateInspectionStatusAsync(Guid inspectionId, InspectionStatus status, CancellationToken cancellationToken = default)
    {
        var payload = new { status };
        var response = await _httpClient.PutAsJsonAsync($"api/inspections/{inspectionId}/status", payload, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task UpdateNotesAsync(Guid inspectionId, string notes, CancellationToken cancellationToken = default)
    {
        var payload = new { notes };
        var response = await _httpClient.PutAsJsonAsync($"api/inspections/{inspectionId}/notes", payload, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
