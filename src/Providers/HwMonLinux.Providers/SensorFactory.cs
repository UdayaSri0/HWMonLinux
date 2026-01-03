using HwMonLinux.Core;

namespace HwMonLinux.Providers;

internal static class SensorFactory
{
    public static SensorReading Create(
        string id,
        IReadOnlyList<string>? groupPath,
        string name,
        SensorType type,
        double? value,
        string unit,
        string source,
        string? textValue = null,
        string? description = null,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new(id, groupPath, name, type, value, unit, source, DateTimeOffset.UtcNow, textValue, description, metadata);
}
