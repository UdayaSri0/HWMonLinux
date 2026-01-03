using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using HwMonLinux.Core;

namespace HwMonLinux.Providers.Services;

public sealed class CompositeSensorProvider : ISensorProvider
{
    private readonly IReadOnlyList<ISensorProvider> _providers;

    public CompositeSensorProvider(IEnumerable<ISensorProvider> providers)
    {
        _providers = providers.ToList();
    }

    public string Name => "Composite";

    public ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken) =>
        new(_providers.Count > 0);

    public async Task<IReadOnlyCollection<SensorReading>> GetSensorReadingsAsync(CancellationToken cancellationToken)
    {
        var result = await CollectAsync(cancellationToken).ConfigureAwait(false);
        return result.Readings;
    }

    public async Task<CompositeSensorResult> CollectAsync(CancellationToken cancellationToken)
    {
        var readings = new List<SensorReading>();
        var diagnostics = new List<ProviderDiagnostic>();

        foreach (var provider in _providers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var stopwatch = Stopwatch.StartNew();
            var status = ProviderStatus.Unavailable;
            var message = default(string);
            IReadOnlyCollection<SensorReading> providerReadings = Array.Empty<SensorReading>();

            try
            {
                var available = await provider.IsAvailableAsync(cancellationToken).ConfigureAwait(false);
                if (!available)
                {
                    message = "Not available";
                }
                else
                {
                    status = ProviderStatus.Success;
                    providerReadings = await provider.GetSensorReadingsAsync(cancellationToken).ConfigureAwait(false) ??
                                       Array.Empty<SensorReading>();
                    readings.AddRange(providerReadings);

                    if (providerReadings.Count == 0)
                    {
                        message = "No readings";
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                status = ProviderStatus.Error;
                message = ex.Message;
            }
            finally
            {
                stopwatch.Stop();
            }

            var diagnostic = new ProviderDiagnostic(provider.Name, status, providerReadings.Count, message, stopwatch.Elapsed);
            diagnostics.Add(diagnostic);
            LogProviderResult(diagnostic);
        }

        return new CompositeSensorResult(readings.AsReadOnly(), diagnostics.AsReadOnly());
    }

    private static void LogProviderResult(ProviderDiagnostic diagnostic)
    {
        var statusText = diagnostic.Status switch
        {
            ProviderStatus.Success => "OK",
            ProviderStatus.Unavailable => "Skipped",
            ProviderStatus.Error => "Error",
            _ => diagnostic.Status.ToString()
        };

        var messageSuffix = string.IsNullOrWhiteSpace(diagnostic.Message) ? string.Empty : $" - {diagnostic.Message}";
        Console.WriteLine($"[{DateTimeOffset.Now:T}] {diagnostic.Name}: {diagnostic.ReadingCount} readings ({statusText}){messageSuffix}");
    }
}

public sealed record CompositeSensorResult(
    IReadOnlyList<SensorReading> Readings,
    IReadOnlyList<ProviderDiagnostic> Diagnostics);
