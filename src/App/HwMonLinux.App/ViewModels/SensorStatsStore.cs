using System;
using System.Collections.Generic;
using HwMonLinux.Core;

namespace HwMonLinux.App.ViewModels;

public sealed class SensorStatsStore
{
    private readonly Dictionary<string, SensorRange> _ranges = new(StringComparer.OrdinalIgnoreCase);

    public (double? min, double? max) Update(SensorReading reading)
    {
        if (!reading.Value.HasValue)
        {
            return _ranges.TryGetValue(reading.Id, out var range)
                ? (range.Min, range.Max)
                : (null, null);
        }

        var current = reading.Value.Value;
        if (_ranges.TryGetValue(reading.Id, out var existing))
        {
            var min = Math.Min(existing.Min, current);
            var max = Math.Max(existing.Max, current);
            _ranges[reading.Id] = existing with { Min = min, Max = max };
            return (min, max);
        }

        var rangeValue = new SensorRange(current, current);
        _ranges[reading.Id] = rangeValue;
        return (current, current);
    }

    public void Reset() => _ranges.Clear();

    private sealed record SensorRange(double Min, double Max);
}
