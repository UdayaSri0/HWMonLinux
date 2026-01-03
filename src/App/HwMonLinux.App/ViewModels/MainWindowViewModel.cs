using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using HwMonLinux.App.Models;
using HwMonLinux.App.Services;
using HwMonLinux.Core;
using HwMonLinux.Providers.Services;

namespace HwMonLinux.App.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly SensorCollectorService _collector;
    private readonly SettingsService _settings;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly ObservableCollection<SensorCardViewModel> _cardCollection = new();
    private readonly ObservableCollection<SensorTreeNode> _rootNodes = new();
    private readonly Dictionary<string, SensorRowViewModel> _rowLookup = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SensorTreeNode> _nodeIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _expandedKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<ProviderDiagnosticViewModel> _providerDiagnostics = new();
    private readonly Dictionary<string, ProviderDiagnosticViewModel> _diagnosticLookup = new(StringComparer.OrdinalIgnoreCase);
    private readonly SensorStatsStore _statsStore = new();
    private readonly List<CardBinding> _cardBindings;
    private readonly IReadOnlyList<ThemeMode> _themeModes = Enum.GetValues<ThemeMode>();
    private bool _isDisposed;
    private double _refreshIntervalSeconds;
    private string _filterText = string.Empty;
    private DateTimeOffset _lastUpdated = DateTimeOffset.MinValue;
    private string? _warningText;
    private ThemeMode _selectedThemeMode;
    private int _totalReadings;
    private int _visibleSensors;
    private int _rootNodeCount;

    public MainWindowViewModel(SensorCollectorService collector, SettingsService settings)
    {
        _collector = collector;
        _settings = settings;
        _refreshIntervalSeconds = Math.Round(_collector.RefreshInterval.TotalSeconds, 1);
        _selectedThemeMode = _settings.Settings.ThemeMode;
        SensorCards = new ReadOnlyObservableCollection<SensorCardViewModel>(_cardCollection);
        RootNodes = new ReadOnlyObservableCollection<SensorTreeNode>(_rootNodes);
        ProviderDiagnostics = new ReadOnlyObservableCollection<ProviderDiagnosticViewModel>(_providerDiagnostics);

        _cardBindings = CreateCardBindings();
        foreach (var binding in _cardBindings)
        {
            _cardCollection.Add(binding.Card);
        }

        ThemeHelper.ApplyTheme(_selectedThemeMode);
        _ = Task.Run(UpdateLoopAsync);
    }

    public ReadOnlyObservableCollection<SensorCardViewModel> SensorCards { get; }

    public ReadOnlyObservableCollection<SensorTreeNode> RootNodes { get; }

    public ReadOnlyObservableCollection<ProviderDiagnosticViewModel> ProviderDiagnostics { get; }

    public IReadOnlyList<ThemeMode> ThemeModes => _themeModes;

    public ThemeMode SelectedThemeMode
    {
        get => _selectedThemeMode;
        set
        {
            if (SetProperty(ref _selectedThemeMode, value))
            {
                _settings.Settings.ThemeMode = value;
                _settings.Save();
                ThemeHelper.ApplyTheme(value);
            }
        }
    }

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

    public int TotalReadings
    {
        get => _totalReadings;
        private set
        {
            if (SetProperty(ref _totalReadings, value))
            {
                OnPropertyChanged(nameof(ShowEmptyState));
            }
        }
    }

    public int VisibleSensors
    {
        get => _visibleSensors;
        private set => SetProperty(ref _visibleSensors, value);
    }

    public int RootNodeCount
    {
        get => _rootNodeCount;
        private set => SetProperty(ref _rootNodeCount, value);
    }

    public bool ShowEmptyState => TotalReadings == 0;

    public string EmptyStateHelp =>
        "No sensor readings yet. Ensure /sys/class/hwmon is present, install lm-sensors and smartmontools (sudo apt install lm-sensors smartmontools; sudo sensors-detect), and keep sysfs hwmon enabled as a fallback.";

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
        TotalReadings = snapshot.TotalSensors;
        WarningText = snapshot.Warnings.Count > 0
            ? string.Join(Environment.NewLine, snapshot.Warnings)
            : null;

        UpdateDiagnostics(snapshot.Diagnostics);
        UpdateCards(snapshot);
        UpdateRows(snapshot);
        RootNodeCount = _rootNodes.Count;
    }

    private void UpdateCards(Snapshot snapshot)
    {
        foreach (var binding in _cardBindings)
        {
            var reading = binding.Selector(snapshot);
            binding.Card.ApplyReading(reading);
        }
    }

    private void UpdateDiagnostics(IReadOnlyList<ProviderDiagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            if (!_diagnosticLookup.TryGetValue(diagnostic.Name, out var viewModel))
            {
                viewModel = new ProviderDiagnosticViewModel(diagnostic.Name);
                _diagnosticLookup[diagnostic.Name] = viewModel;
                _providerDiagnostics.Add(viewModel);
            }

            viewModel.Update(diagnostic);
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
            EnsureNodeForRow(row);
        }

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var filter = FilterText?.Trim();
        var visibleCount = 0;
        foreach (var node in _rootNodes)
        {
            UpdateVisibility(node, filter, ref visibleCount);
        }

        VisibleSensors = visibleCount;
    }

    private void EnsureNodeForRow(SensorRowViewModel row)
    {
        var segments = row.GroupPath.Count > 0
            ? row.GroupPath
            : ImmutableArray.Create("Ungrouped");

        SensorTreeNode? parent = null;
        var pathSegments = new List<string>();
        foreach (var segment in segments)
        {
            pathSegments.Add(segment);
            var groupKey = string.Join("/", pathSegments);
            parent = GetOrCreateNode(groupKey, segment, null, parent);
        }

        pathSegments.Add(row.Name);
        var leafKey = string.Join("/", pathSegments);
        GetOrCreateNode(leafKey, row.Name, row, parent);
    }

    private SensorTreeNode GetOrCreateNode(string key, string title, SensorRowViewModel? row, SensorTreeNode? parent)
    {
        if (_nodeIndex.TryGetValue(key, out var node))
        {
            if (row is not null)
            {
                node.AttachSensor(row);
            }

            return node;
        }

        var isGroup = row is null;
        var expanded = isGroup && _expandedKeys.Contains(key);
        if (isGroup && !_expandedKeys.Contains(key))
        {
            expanded = true;
            _expandedKeys.Add(key);
        }

        var newNode = new SensorTreeNode(key, title, row, expanded)
        {
            IsVisible = true
        };

        if (isGroup && newNode.IsExpanded)
        {
            _expandedKeys.Add(key);
        }

        newNode.PropertyChanged += OnNodePropertyChanged;
        _nodeIndex[key] = newNode;

        if (parent is null)
        {
            InsertNodeSorted(_rootNodes, newNode);
        }
        else
        {
            InsertNodeSorted(parent.Children, newNode);
        }

        return newNode;
    }

    private void InsertNodeSorted(IList<SensorTreeNode> collection, SensorTreeNode node)
    {
        var index = 0;
        while (index < collection.Count && CompareNodes(collection[index], node) <= 0)
        {
            index++;
        }

        collection.Insert(index, node);
    }

    private static int CompareNodes(SensorTreeNode left, SensorTreeNode right)
    {
        var groupComparison = left.IsLeaf.CompareTo(right.IsLeaf);
        if (groupComparison != 0)
        {
            return groupComparison;
        }

        return string.Compare(left.Title, right.Title, StringComparison.OrdinalIgnoreCase);
    }

    private bool UpdateVisibility(SensorTreeNode node, string? filter, ref int visibleCount)
    {
        var hasVisibleChild = false;
        foreach (var child in node.Children)
        {
            hasVisibleChild |= UpdateVisibility(child, filter, ref visibleCount);
        }

        var matches = string.IsNullOrWhiteSpace(filter)
            || (node.IsLeaf && node.Sensor?.MatchesFilter(filter) == true)
            || (!node.IsLeaf && filter is not null && node.Title.Contains(filter, StringComparison.OrdinalIgnoreCase));

        var isVisible = node.IsLeaf ? matches : matches || hasVisibleChild;
        node.IsVisible = isVisible;
        if (node.IsLeaf && isVisible)
        {
            visibleCount++;
        }

        return isVisible;
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not SensorTreeNode node)
        {
            return;
        }

        if (e.PropertyName == nameof(SensorTreeNode.IsExpanded))
        {
            if (node.IsExpanded)
            {
                _expandedKeys.Add(node.Key);
            }
            else
            {
                _expandedKeys.Remove(node.Key);
            }
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
