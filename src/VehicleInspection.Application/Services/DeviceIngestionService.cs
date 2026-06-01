using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using VehicleInspection.Application.Models;
using VehicleInspection.Application.Repositories;

namespace VehicleInspection.Application.Services;

public sealed class DeviceIngestionService
{
    private static readonly HashSet<string> AllowedImageFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "png", "jpg", "jpeg", "bmp", "tif", "tiff"
    };

    private readonly IInspectionRepository _repository;
    private readonly string _apiKey;
    private readonly string _imageStorageRoot;
    private readonly string? _imageBaseUrl;
    private readonly ConcurrentDictionary<string, IngestionRecordState> _states = new(StringComparer.OrdinalIgnoreCase);

    public DeviceIngestionService(IInspectionRepository repository, string apiKey, string imageStorageRoot, string? imageBaseUrl = null)
    {
        _repository = repository;
        _apiKey = apiKey;
        _imageStorageRoot = imageStorageRoot;
        _imageBaseUrl = imageBaseUrl?.TrimEnd('/');
        Directory.CreateDirectory(_imageStorageRoot);
    }

    public event EventHandler<InspectionRecord>? InspectionUpdated;
    public event EventHandler<string>? MessageIgnored;

    public async Task<InspectionRecord?> ProcessJsonAsync(string json, CancellationToken cancellationToken = default)
    {
        var message = JsonSerializer.Deserialize<DeviceIngestionMessage>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidDataException("Device ingestion message is empty.");

        return await ProcessAsync(message, cancellationToken);
    }

    public async Task<InspectionRecord?> ProcessAsync(DeviceIngestionMessage message, CancellationToken cancellationToken = default)
    {
        Validate(message);
        var category = Enum.Parse<DeviceImageCategory>(message.Category, ignoreCase: true);
        var state = _states.GetOrAdd(message.TriggerId, CreateState);

        if (!state.TryAccept(category))
        {
            MessageIgnored?.Invoke(this, $"Duplicate {category} ignored for trigger {message.TriggerId}.");
            return null;
        }

        var imagePath = SaveImage(message, category);
        state.Record = ApplyMessage(state.Record, message, category, imagePath);
        await _repository.UpsertInspectionAsync(state.Record, cancellationToken);
        InspectionUpdated?.Invoke(this, state.Record);
        return state.Record;
    }

    private IngestionRecordState CreateState(string triggerId)
    {
        var record = new InspectionRecord
        {
            TriggerId = triggerId,
            ScanTime = DateTimeOffset.Now,
            LicensePlate = "Pending OCR",
            LicensePlateHash = ComputeHash("Pending OCR"),
            Status = InspectionStatus.Pending,
            UnderVehicleImagePath = string.Empty,
            FullVehicleImagePath = string.Empty,
            LicensePlateImagePath = string.Empty,
            XrayImagePath = null,
            FodAlerts = Array.Empty<FodAlert>(),
            OperatorName = "Device ingestion",
            Lane = "Pending lane",
            Notes = "Record created from external device trigger.",
            SystemHealth = "Socket ingestion active",
            SystemErrors = Array.Empty<SystemErrorMessage>()
        };

        return new IngestionRecordState(triggerId, record);
    }

    private static InspectionRecord ApplyMessage(InspectionRecord record, DeviceIngestionMessage message, DeviceImageCategory category, string imagePath)
    {
        return category switch
        {
            DeviceImageCategory.Uvss => Clone(record, underVehicleImagePath: imagePath, lane: message.LaneId, fodAlerts: ToFodAlerts(message.FodJson), status: message.FodJson?.Alerts.Count > 0 ? InspectionStatus.Review : InspectionStatus.Pending),
            DeviceImageCategory.Xray => Clone(record, xrayImagePath: imagePath, lane: message.LaneId),
            DeviceImageCategory.Vlpr => Clone(record, licensePlateImagePath: imagePath, licensePlate: string.IsNullOrWhiteSpace(message.LicensePlate) ? record.LicensePlate : message.LicensePlate.Trim(), licensePlateHash: ComputeHash(string.IsNullOrWhiteSpace(message.LicensePlate) ? record.LicensePlate : message.LicensePlate.Trim()), lane: message.LaneId),
            _ => record
        };
    }

    private static InspectionRecord Clone(
        InspectionRecord record,
        string? underVehicleImagePath = null,
        string? fullVehicleImagePath = null,
        string? licensePlateImagePath = null,
        string? xrayImagePath = null,
        IReadOnlyList<FodAlert>? fodAlerts = null,
        string? licensePlate = null,
        string? licensePlateHash = null,
        string? lane = null,
        InspectionStatus? status = null)
    {
        return new InspectionRecord
        {
            Id = record.Id,
            TriggerId = record.TriggerId,
            ScanTime = record.ScanTime,
            LicensePlate = licensePlate ?? record.LicensePlate,
            LicensePlateHash = licensePlateHash ?? record.LicensePlateHash,
            Status = status ?? record.Status,
            UnderVehicleImagePath = underVehicleImagePath ?? record.UnderVehicleImagePath,
            FullVehicleImagePath = fullVehicleImagePath ?? record.FullVehicleImagePath,
            LicensePlateImagePath = licensePlateImagePath ?? record.LicensePlateImagePath,
            XrayImagePath = xrayImagePath ?? record.XrayImagePath,
            FodAlerts = fodAlerts ?? record.FodAlerts,
            OperatorName = record.OperatorName,
            Lane = lane ?? record.Lane,
            Notes = record.Notes,
            SystemHealth = record.SystemHealth,
            SystemErrors = record.SystemErrors
        };
    }

    private static IReadOnlyList<FodAlert> ToFodAlerts(FodPayload? payload)
    {
        if (payload == null)
        {
            return Array.Empty<FodAlert>();
        }

        return payload.Alerts.Select(alert => new FodAlert
        {
            Zone = Limit(alert.Zone, 64),
            Severity = NormalizeSeverity(alert.Severity),
            Description = Limit(alert.Description, 512),
            Confidence = Math.Clamp(alert.Confidence, 0, 1)
        }).ToList();
    }

    private string SaveImage(DeviceIngestionMessage message, DeviceImageCategory category)
    {
        var bytes = Convert.FromBase64String(message.ImageBase64);
        if (bytes.Length > 8 * 1024 * 1024)
        {
            throw new InvalidDataException("Image payload exceeds the 8 MB limit.");
        }

        var triggerDirectory = Path.Combine(_imageStorageRoot, SafeSegment(message.TriggerId));
        Directory.CreateDirectory(triggerDirectory);
        var extension = message.ImageFormat.Equals("jpeg", StringComparison.OrdinalIgnoreCase) ? "jpg" : message.ImageFormat.ToLowerInvariant();
        var fileName = $"{category.ToString().ToLowerInvariant()}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.{extension}";
        var path = Path.Combine(triggerDirectory, fileName);
        File.WriteAllBytes(path, bytes);
        return _imageBaseUrl == null ? path : $"{_imageBaseUrl}/{SafeSegment(message.TriggerId)}/{fileName}";
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
    }

    private static void Require(string value, int maxLength, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
        {
            throw new InvalidDataException($"{name} is required and must be {maxLength} characters or fewer.");
        }
    }

    private static string SafeSegment(string value)
    {
        return string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));
    }

    private static string Limit(string value, int maxLength)
    {
        value = value.Trim();
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static string NormalizeSeverity(string severity)
    {
        return severity.Trim().ToLowerInvariant() switch
        {
            "critical" => "Critical",
            "high" => "High",
            "medium" => "Medium",
            "low" => "Low",
            _ => "Medium"
        };
    }

    private static string ComputeHash(string value)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value.ToUpperInvariant()));
        return Convert.ToHexString(bytes);
    }
}
