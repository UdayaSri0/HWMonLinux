using System.Collections.Concurrent;
using System.Globalization;

namespace HwMonLinux.Core;

/// <summary>
/// Represents a single sensor reading value captured at a point in time.
/// </summary>
public sealed record SensorReading(
    string Id,
    string Name,
    SensorType Type,
    double? Value,
    string Unit,
    string Source,
    string? TextValue = null,
    string? Description = null,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    private static readonly ConcurrentDictionary<string, Func<double, string>> Formatters = new();

    public string FormattedValue => TextValue ?? (Value.HasValue
        ? FormatValue(Value.Value, Unit)
        : "Not available");

    private static string FormatValue(double value, string unit)
    {
        var formatter = Formatters.GetOrAdd(unit, CreateFormatter);
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
        "%" => v => $"{v:F1} %",
        _ => v => $"{v.ToString(CultureInfo.InvariantCulture)} {unit}".Trim()
    };
}
