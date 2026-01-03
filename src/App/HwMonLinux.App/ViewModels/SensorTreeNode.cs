using System.Collections.ObjectModel;
using System.ComponentModel;

namespace HwMonLinux.App.ViewModels;

public sealed class SensorTreeNode : ViewModelBase
{
    private SensorRowViewModel? _sensor;
    private string _title;
    private bool _isExpanded;
    private bool _isVisible = true;

    public SensorTreeNode(string key, string title, SensorRowViewModel? sensor = null, bool isExpanded = false)
    {
        Key = key;
        _title = title;
        _sensor = sensor;
        if (_sensor is not null)
        {
            _sensor.PropertyChanged += OnSensorPropertyChanged;
        }
        _isExpanded = isExpanded;
        Children = new ObservableCollection<SensorTreeNode>();
    }

    public string Key { get; }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public SensorRowViewModel? Sensor => _sensor;

    public ObservableCollection<SensorTreeNode> Children { get; }

    public bool IsLeaf => _sensor is not null;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    public string DisplayValue => _sensor?.Value ?? string.Empty;
    public string DisplayMin => _sensor?.Min ?? string.Empty;
    public string DisplayMax => _sensor?.Max ?? string.Empty;

    public void AttachSensor(SensorRowViewModel sensor)
    {
        if (_sensor == sensor)
        {
            return;
        }

        if (_sensor is not null)
        {
            _sensor.PropertyChanged -= OnSensorPropertyChanged;
        }

        _sensor = sensor;
        _sensor.PropertyChanged += OnSensorPropertyChanged;
        Title = sensor.Name;
        OnPropertyChanged(nameof(Sensor));
        OnPropertyChanged(nameof(DisplayValue));
        OnPropertyChanged(nameof(DisplayMin));
        OnPropertyChanged(nameof(DisplayMax));
    }

    private void OnSensorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SensorRowViewModel.Value))
        {
            OnPropertyChanged(nameof(DisplayValue));
        }
        else if (e.PropertyName == nameof(SensorRowViewModel.Min))
        {
            OnPropertyChanged(nameof(DisplayMin));
        }
        else if (e.PropertyName == nameof(SensorRowViewModel.Max))
        {
            OnPropertyChanged(nameof(DisplayMax));
        }
        else if (e.PropertyName == nameof(SensorRowViewModel.Name))
        {
            Title = _sensor?.Name ?? Title;
        }
    }
}
