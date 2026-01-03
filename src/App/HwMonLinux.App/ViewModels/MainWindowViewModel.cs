using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using HwMonLinux.Core;
using HwMonLinux.Providers.Services;

namespace HwMonLinux.App.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly SensorCollectorService _collector;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly ObservableCollection<SensorCardViewModel> _cardCollection = new();
    private readonly ObservableCollection<SensorTreeNode> _rootNodes = new();
    private readonly Dictionary<string, SensorRowViewModel> _rowLookup = new(StringComparer.OrdinalIgnoreCase);
    private readonly SensorStatsStore _statsStore = new();
    private readonly List<CardBinding> _cardBindings;
    private bool _isDisposed;
    private double _refreshIntervalSeconds;
    private string _filterText = string.Empty;
    private DateTimeOffset _lastUpdated = DateTimeOffset.MinValue;
    private string? _warningText;

    public MainWindowViewModel(SensorCollectorService collector)
    {
        _collector = collector;
        _refreshIntervalSeconds = Math.Round(_collector.RefreshInterval.TotalSeconds, 1);
        SensorCards = new ReadOnlyObservableCollection<SensorCardViewModel>(_cardCollection);
        RootNodes = new ReadOnlyObservableCollection<SensorTreeNode>(_rootNodes);

        _cardBindings = CreateCardBindings();
        foreach (var binding in _cardBindings)
        {
            _cardCollection.Add(binding.Card);
        }

        _ = Task.Run(UpdateLoopAsync);
    }

    public ReadOnlyObservableCollection<SensorCardViewModel> SensorCards { get; }

    public ReadOnlyObservableCollection<SensorTreeNode> RootNodes { get; }

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

    [RelayCommand]
    private void ResetStats()
    {
        _statsStore.Reset();
        foreach (var row in _rowLookup.Values)
        {
            row.ApplyStats(null, null);
        }

        ApplyFilter();
    }

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
            // Expected when shutting down.
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
            var (min, max) = _statsStore.Update(reading);
            row.ApplyStats(min, max);
        }

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var filter = FilterText?.Trim();
        var rows = _rowLookup.Values
            .Where(row => row.MatchesFilter(filter))
            .ToList();

        var nodes = BuildTree(rows);
        SynchronizeTree(nodes);
    }

    private IReadOnlyList<SensorTreeNode> BuildTree(IReadOnlyList<SensorRowViewModel> rows)
    {
        var root = new SensorTreeNode("root");
        foreach (var row in rows.OrderBy(r => string.Join("/", r.GroupPath)).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
        {
            var segments = row.GroupPath.Count > 0
                ? row.GroupPath
                : ImmutableArray.Create("Ungrouped");

            var parent = root;
            foreach (var segment in segments)
            {
                parent = parent.GetOrAddChild(segment);
            }

            parent.Children.Add(new SensorTreeNode(row.Name, row));
        }

        foreach (var child in root.Children)
        {
            child.SortChildren();
        }

        return root.Children.ToList();
    }

    private void SynchronizeTree(IReadOnlyList<SensorTreeNode> nodes)
    {
        _rootNodes.Clear();
        foreach (var node in nodes)
        {
            _rootNodes.Add(node);
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
