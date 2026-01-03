using System;
using System.Linq;
using System.Threading.Channels;
using HwMonLinux.Core;

namespace HwMonLinux.Providers.Services;

public sealed class SensorCollectorService : IAsyncDisposable
{
    private readonly CompositeSensorProvider _compositeProvider;
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
        var providerList = providers.ToList();
        _compositeProvider = new CompositeSensorProvider(providerList);
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
            var snapshot = await CollectSnapshotAsync(cancellationToken).ConfigureAwait(false);
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

    private async Task<Snapshot> CollectSnapshotAsync(CancellationToken cancellationToken)
    {
        var result = await _compositeProvider.CollectAsync(cancellationToken).ConfigureAwait(false);
        var warnings = result.Diagnostics
            .Where(d => d.Status != ProviderStatus.Success)
            .Select(d => $"{d.Name}: {d.Message ?? d.Status.ToString()}")
            .ToList();

        return new Snapshot(
            DateTimeOffset.UtcNow,
            result.Readings,
            warnings.AsReadOnly(),
            result.Diagnostics);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _cts?.Dispose();
    }
}
