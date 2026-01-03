using System.Collections.ObjectModel;
using System.Threading.Channels;
using HwMonLinux.Core;

namespace HwMonLinux.Providers.Services;

public sealed class SensorCollectorService : IAsyncDisposable
{
    private readonly IReadOnlyList<ISensorProvider> _providers;
    private readonly Channel<Snapshot> _channel = Channel.CreateUnbounded<Snapshot>(new UnboundedChannelOptions
    {
        SingleWriter = true,
        AllowSynchronousContinuations = false
    });
    private readonly object _intervalLock = new();
    private TimeSpan _currentInterval;
    private Task? _worker;
    private CancellationTokenSource? _cts;

    public SensorCollectorService(IEnumerable<ISensorProvider> providers, RefreshOptions? options = null)
    {
        _providers = providers.ToList();
        _currentInterval = (options ?? RefreshOptions.Default).Interval;
    }

    public Snapshot LatestSnapshot { get; private set; } = Snapshot.Empty;

    public TimeSpan RefreshInterval
    {
        get
        {
            lock (_intervalLock)
            {
                return _currentInterval;
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_worker is not null)
        {
            return Task.CompletedTask;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _worker = Task.Run(() => RunAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_cts is null)
        {
            return;
        }

        _cts.Cancel();

        if (_worker is not null)
        {
            try
            {
                await _worker.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected when shutting down.
            }
        }
    }

    public void UpdateInterval(TimeSpan interval)
    {
        var clamped = interval;
        if (clamped < RefreshOptions.MinimumInterval)
        {
            clamped = RefreshOptions.MinimumInterval;
        }
        else if (clamped > RefreshOptions.MaximumInterval)
        {
            clamped = RefreshOptions.MaximumInterval;
        }

        lock (_intervalLock)
        {
            _currentInterval = clamped;
        }
    }

    public IAsyncEnumerable<Snapshot> GetSnapshotsAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var (sensors, warnings) = await CollectAsync(cancellationToken).ConfigureAwait(false);
            var snapshot = new Snapshot(DateTimeOffset.UtcNow, sensors, warnings);
            LatestSnapshot = snapshot;
            await _channel.Writer.WriteAsync(snapshot, cancellationToken).ConfigureAwait(false);

            var delay = RefreshInterval;
            if (delay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _channel.Writer.TryComplete();
    }

    private async Task<(IReadOnlyList<SensorReading> sensors, IReadOnlyList<string> warnings)> CollectAsync(CancellationToken cancellationToken)
    {
        var readings = new List<SensorReading>();
        var warnings = new List<string>();

        foreach (var provider in _providers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (!await provider.IsAvailableAsync(cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                var providerReadings = await provider.GetSensorReadingsAsync(cancellationToken).ConfigureAwait(false);
                readings.AddRange(providerReadings);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                warnings.Add($"{provider.Name}: {ex.Message}");
            }
        }

        return (readings.AsReadOnly(), warnings.AsReadOnly());
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _cts?.Dispose();
    }
}
