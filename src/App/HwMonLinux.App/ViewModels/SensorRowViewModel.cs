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

    public void UpdateFromReading(SensorReading reading)
    {
        Name = reading.Name;
        Value = reading.FormattedValue;
        Type = reading.Type.ToString();
        Source = reading.Source;
        Description = reading.Description;
    }
}
