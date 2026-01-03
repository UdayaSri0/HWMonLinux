namespace HwMonLinux.Core;

/// <summary>
/// Contract implemented by any service that knows how to query a set of sensors.
/// </summary>
public interface ISensorProvider
{
    string Name { get; }

    ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken);

    Task<IReadOnlyCollection<SensorReading>> GetSensorReadingsAsync(CancellationToken cancellationToken);
}
