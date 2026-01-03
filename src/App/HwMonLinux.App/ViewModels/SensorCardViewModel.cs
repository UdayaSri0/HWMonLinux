using CommunityToolkit.Mvvm.ComponentModel;
using HwMonLinux.Core;

namespace HwMonLinux.App.ViewModels;

public sealed partial class SensorCardViewModel : ObservableObject
{
    public SensorCardViewModel(string id, string title)
    {
        Id = id;
        Title = title;
    }

    public string Id { get; }

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private string _value = "Not available";

    [ObservableProperty]
    private string? _subtitle;

    public void ApplyReading(SensorReading? reading)
    {
        if (reading is null)
        {
            Value = "Not available";
            Subtitle = null;
            return;
        }

        Value = reading.FormattedValue;
        Subtitle = reading.Description;
    }
}
