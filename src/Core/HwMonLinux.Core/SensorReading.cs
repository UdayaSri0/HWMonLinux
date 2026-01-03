using System.Collections.Concurrent;
using System.Globalization;

namespace HwMonLinux.Core;

/// <summary>
/// Represents a single sensor reading value captured at a point in time.
/// </summary>
public sealed class SensorReading
{
    private static readonly ConcurrentDictionary<string, Func<double, string>> Formatters = new();

    public SensorReading(
        string id,
        IReadOnlyList<string>? groupPath,
        string name,
        SensorType type,
        double? value,
        string unit,
        string source,
        DateTimeOffset timestamp,
        string? textValue = null,
        string? description = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        Id = id;
        GroupPath = groupPath is null
            ? Array.Empty<string>()
            : groupPath.Where(segment => !string.IsNullOrWhiteSpace(segment))
                .Select(segment => segment.Trim())
                .ToArray();
        Name = name;
        Type = type;
        Value = value;
        Unit = unit;
        Source = source;
        Timestamp = timestamp;
        TextValue = textValue;
        Description = description;
        Metadata = metadata;
    }

    public string Id { get; }
    public IReadOnlyList<string> GroupPath { get; }
    public string Name { get; }
    public SensorType Type { get; }
    public double? Value { get; }
    public string Unit { get; }
    public string Source { get; }
    public DateTimeOffset Timestamp { get; }
    public string? TextValue { get; }
    public string? Description { get; }
    public IReadOnlyDictionary<string, string>? Metadata { get; }

    public string GroupKey => GroupPath.Count == 0
        ? "Ungrouped"
        : string.Join("/", GroupPath);

    public string FormattedValue => TextValue ?? (Value.HasValue
        ? Format(Value.Value, Unit)
        : "Not available");

    public static string Format(double value, string unit)
    {
        var formatter = Formatters.GetOrAdd(unit ?? string.Empty, CreateFormatter);
        return formatter(value);
    }

    private static Func<double, string> CreateFormatter(string unit) => unit switch
    {
        "C" => v => $"{v:F1} °C",
        "MHz" => v => $"{v:F0} MHz",
        "GHz" => v => $"{v:F2} GHz",
        "RPM" => v => $"{v:F0} rpm",
        "GB" => v => $"{v:F1} GB",
        "GiB" => v => $"{v:F1} GiB",
        "W" => v => $"{v:F1} W",
        "V" => v => $"{v:F3} V",
        "A" => v => $"{v:F2} A",
        "mWh" => v => $"{v:F0} mWh",
        "%" => v => $"{v:F1} %",
        _ => v => $"{v.ToString(CultureInfo.InvariantCulture)} {unit}".Trim()
    };
}
