using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using HwMonLinux.Core;

namespace HwMonLinux.App.ViewModels;

public sealed partial class SensorRowViewModel : ObservableObject
{
    public SensorRowViewModel(string id)
    {
        Id = id;
    }

    public string Id { get; }
    private string[] _groupPath = Array.Empty<string>();
    private string _unit = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _value = "Not available";

    [ObservableProperty]
    private string _type = string.Empty;

    [ObservableProperty]
    private string _source = string.Empty;

    [ObservableProperty]
    private string? _description;

    [ObservableProperty]
    private string _min = "--";

    [ObservableProperty]
    private string _max = "--";

    public IReadOnlyList<string> GroupPath => _groupPath;

    public string GroupDisplay => _groupPath.Length == 0
        ? "Ungrouped"
        : string.Join(" / ", _groupPath);

    public void UpdateFromReading(SensorReading reading)
    {
        _groupPath = reading.GroupPath.ToArray();
        _unit = reading.Unit;
        Name = reading.Name;
        Value = reading.FormattedValue;
        Type = reading.Type.ToString();
        Source = reading.Source;
        Description = reading.Description;
    }

    public void ApplyStats(double? minValue, double? maxValue)
    {
        Min = minValue.HasValue ? SensorReading.Format(minValue.Value, _unit) : "--";
        Max = maxValue.HasValue ? SensorReading.Format(maxValue.Value, _unit) : "--";
    }

    public bool MatchesFilter(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               Type.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               Source.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               GroupDisplay.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               (Description?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);
    }
}
