namespace VehicleInspection.Application.Models;

public sealed class IngestionRecordState
{
    private readonly HashSet<DeviceImageCategory> _receivedCategories = new();

    public IngestionRecordState(string triggerId, InspectionRecord record)
    {
        TriggerId = triggerId;
        Record = record;
    }

    public string TriggerId { get; }
    public InspectionRecord Record { get; set; }
    public DateTimeOffset CreatedUtc { get; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastUpdatedUtc { get; private set; } = DateTimeOffset.UtcNow;
    public IReadOnlySet<DeviceImageCategory> ReceivedCategories => _receivedCategories;

    public bool TryAccept(DeviceImageCategory category)
    {
        var accepted = _receivedCategories.Add(category);
        if (accepted)
        {
            LastUpdatedUtc = DateTimeOffset.UtcNow;
        }

        return accepted;
    }
}
