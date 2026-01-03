using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using HwMonLinux.Core;
using HwMonLinux.Providers.Services;

namespace HwMonLinux.App.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly SensorCollectorService _collector;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly ObservableCollection<SensorCardViewModel> _cardCollection = new();
    private readonly ObservableCollection<SensorRowViewModel> _filteredRows = new();
    private readonly Dictionary<string, SensorRowViewModel> _rowLookup = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CardBinding> _cardBindings;
    private double _refreshIntervalSeconds;
    private bool _isDisposed;

    private string _filterText = string.Empty;
    private DateTimeOffset _lastUpdated = DateTimeOffset.MinValue;
    private string? _warningText;

    public MainWindowViewModel(SensorCollectorService collector)
    {
        _collector = collector;
        _refreshIntervalSeconds = Math.Round(_collector.RefreshInterval.TotalSeconds, 1);
        SensorCards = new ReadOnlyObservableCollection<SensorCardViewModel>(_cardCollection);
        Sensors = new ReadOnlyObservableCollection<SensorRowViewModel>(_filteredRows);

        _cardBindings = CreateCardBindings();
        foreach (var binding in _cardBindings)
        {
            _cardCollection.Add(binding.Card);
        }

        _ = Task.Run(UpdateLoopAsync);
    }

    public ReadOnlyObservableCollection<SensorCardViewModel> SensorCards { get; }

    public ReadOnlyObservableCollection<SensorRowViewModel> Sensors { get; }

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value))
            {
                ApplyFilter();
            }
        }
    }

    public double RefreshIntervalSeconds
    {
        get => _refreshIntervalSeconds;
        set
        {
            var clamped = Math.Clamp(
                value,
                RefreshOptions.MinimumInterval.TotalSeconds,
                RefreshOptions.MaximumInterval.TotalSeconds);

            if (SetProperty(ref _refreshIntervalSeconds, clamped))
            {
                _collector.UpdateInterval(TimeSpan.FromSeconds(clamped));
            }
        }
    }

    public DateTimeOffset LastUpdated
    {
        get => _lastUpdated;
        private set => SetProperty(ref _lastUpdated, value);
    }

    public string? WarningText
    {
        get => _warningText;
        private set
        {
            if (SetProperty(ref _warningText, value))
            {
                OnPropertyChanged(nameof(HasWarnings));
            }
        }
    }

    public bool HasWarnings => !string.IsNullOrWhiteSpace(_warningText);

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
    }

    private async Task UpdateLoopAsync()
    {
        try
        {
            await foreach (var snapshot in _collector.GetSnapshotsAsync(_cancellationTokenSource.Token))
            {
                await Dispatcher.UIThread.InvokeAsync(() => ApplySnapshot(snapshot));
            }
        }
        catch (OperationCanceledException)
        {
            // Ignored on shutdown.
        }
    }

    private void ApplySnapshot(Snapshot snapshot)
    {
        LastUpdated = snapshot.Timestamp;
        WarningText = snapshot.Warnings.Count > 0
            ? string.Join(Environment.NewLine, snapshot.Warnings)
            : null;

        UpdateCards(snapshot);
        UpdateRows(snapshot);
    }

    private void UpdateCards(Snapshot snapshot)
    {
        foreach (var binding in _cardBindings)
        {
            var reading = binding.Selector(snapshot);
            binding.Card.ApplyReading(reading);
        }
    }

    private void UpdateRows(Snapshot snapshot)
    {
        foreach (var reading in snapshot.Sensors)
        {
            if (!_rowLookup.TryGetValue(reading.Id, out var row))
            {
                row = new SensorRowViewModel(reading.Id);
                _rowLookup[reading.Id] = row;
            }

            row.UpdateFromReading(reading);
        }

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var filter = FilterText?.Trim();
        IEnumerable<SensorRowViewModel> source = _rowLookup.Values;

        if (!string.IsNullOrWhiteSpace(filter))
        {
            source = source.Where(row =>
                row.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                row.Type.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                row.Source.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                (row.Description?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        var ordered = source
            .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        SynchronizeCollection(_filteredRows, ordered);
    }

    private static void SynchronizeCollection<T>(ObservableCollection<T> target, IReadOnlyList<T> items)
    {
        target.Clear();
        for (var i = 0; i < items.Count; i++)
        {
            target.Add(items[i]);
        }
    }

    private List<CardBinding> CreateCardBindings() =>
        new()
        {
            new(new SensorCardViewModel("cpu-temp", "CPU Temp"), snapshot =>
                snapshot.Sensors.FirstOrDefault(s => s.Type == SensorType.CpuTemperature)),
            new(new SensorCardViewModel("cpu-load", "CPU Load"), snapshot =>
                snapshot.Sensors.FirstOrDefault(s => s.Id.Equals("cpu.load.cpu", StringComparison.OrdinalIgnoreCase)) ??
                snapshot.Sensors.FirstOrDefault(s => s.Type == SensorType.CpuLoad)),
            new(new SensorCardViewModel("ram-usage", "RAM Usage"), snapshot =>
                snapshot.Sensors.FirstOrDefault(s => s.Id.Equals("memory.usage.percent", StringComparison.OrdinalIgnoreCase)))
        };

    private sealed record CardBinding(SensorCardViewModel Card, Func<Snapshot, SensorReading?> Selector);
}
