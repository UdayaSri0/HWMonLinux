namespace HwMonLinux.Core;

/// <summary>
/// Represents a collection of sensor readings captured together.
/// </summary>
public sealed record Snapshot(
    DateTimeOffset Timestamp,
    IReadOnlyList<SensorReading> Sensors,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<ProviderDiagnostic> Diagnostics)
{
    public static Snapshot Empty { get; } = new(
        DateTimeOffset.MinValue,
        Array.Empty<SensorReading>(),
        Array.Empty<string>(),
        Array.Empty<ProviderDiagnostic>());

    public int TotalSensors => Sensors.Count;

    public IEnumerable<SensorReading> OfType(SensorType type) =>
        Sensors.Where(s => s.Type == type);
}
