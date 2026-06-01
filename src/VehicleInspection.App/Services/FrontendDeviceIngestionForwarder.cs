using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using VehicleInspection.Application.Models;

namespace VehicleInspection.App.Services;

public sealed class FrontendDeviceIngestionForwarder
{
    private static readonly HashSet<string> AllowedImageFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "png", "jpg", "jpeg", "bmp", "tif", "tiff"
    };

    private readonly BackendInspectionClient _client;
    private readonly string _apiKey;
    private readonly ConcurrentDictionary<string, HashSet<DeviceImageCategory>> _receivedCategories = new(StringComparer.OrdinalIgnoreCase);

    public FrontendDeviceIngestionForwarder(BackendInspectionClient client, string apiKey)
    {
        _client = client;
        _apiKey = apiKey;
    }

    public event EventHandler<InspectionRecord>? InspectionUpdated;
    public event EventHandler<string>? MessageIgnored;

    public async Task<InspectionRecord?> ProcessJsonAsync(string json, CancellationToken cancellationToken = default)
    {
        var message = JsonSerializer.Deserialize<DeviceIngestionMessage>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidDataException("Device ingestion message is empty.");

        Validate(message);
        var category = Enum.Parse<DeviceImageCategory>(message.Category, ignoreCase: true);
        var categories = _receivedCategories.GetOrAdd(message.TriggerId, _ => new HashSet<DeviceImageCategory>());

        lock (categories)
        {
            if (!categories.Add(category))
            {
                MessageIgnored?.Invoke(this, $"Duplicate {category} ignored at frontend for trigger {message.TriggerId}.");
                return null;
            }
        }

        InspectionRecord? record = await _client.ForwardDeviceMessageAsync(message, cancellationToken);
        if (record == null)
        {
            MessageIgnored?.Invoke(this, $"Duplicate {category} ignored by backend for trigger {message.TriggerId}.");
            return null;
        }

        InspectionUpdated?.Invoke(this, record);
        return record;
    }

    private void Validate(DeviceIngestionMessage message)
    {
        if (!string.Equals(message.ApiKey, _apiKey, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Invalid device ingestion API key.");
        }

        Require(message.TriggerId, 96, nameof(message.TriggerId));
        Require(message.Category, 16, nameof(message.Category));
        Require(message.DeviceId, 96, nameof(message.DeviceId));
        Require(message.LaneId, 96, nameof(message.LaneId));
        Require(message.ImageFormat, 8, nameof(message.ImageFormat));

        if (!Enum.TryParse<DeviceImageCategory>(message.Category, true, out _))
        {
            throw new InvalidDataException("Unsupported device image category.");
        }

        if (!AllowedImageFormats.Contains(message.ImageFormat))
        {
            throw new InvalidDataException("Unsupported image format.");
        }

        if (string.IsNullOrWhiteSpace(message.ImageBase64))
        {
            throw new InvalidDataException("Image payload is required.");
        }

        if (Convert.FromBase64String(message.ImageBase64).Length > 8 * 1024 * 1024)
        {
            throw new InvalidDataException("Image payload exceeds the 8 MB limit.");
        }
    }

    private static void Require(string value, int maxLength, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
        {
            throw new InvalidDataException($"{name} is required and must be {maxLength} characters or fewer.");
        }
    }
}
