using System.Linq;
using System.Text.Json;
using HwMonLinux.Core;
using HwMonLinux.Providers.Abstractions;
using HwMonLinux.Providers.Parsers;

namespace HwMonLinux.Providers.Providers;

public sealed class DiskSmartProvider : ISensorProvider
{
    private readonly IProcessRunner _processRunner;

    public DiskSmartProvider(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public string Name => "smartctl";

    public async ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        var smartctlAvailable = await _processRunner.IsAvailableAsync("smartctl", cancellationToken).ConfigureAwait(false);
        if (!smartctlAvailable)
        {
            return false;
        }

        return await _processRunner.IsAvailableAsync("lsblk", cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<SensorReading>> GetSensorReadingsAsync(CancellationToken cancellationToken)
    {
        var disks = await GetDisksAsync(cancellationToken).ConfigureAwait(false);
        if (disks.Count == 0)
        {
            return Array.Empty<SensorReading>();
        }

        var sensors = new List<SensorReading>();
        foreach (var disk in disks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var args = $"-j -a /dev/{disk.Name}";
            var result = await _processRunner.RunAsync("smartctl", args, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                continue;
            }

            var status = SmartCtlParser.Parse(result.StandardOutput);
            if (status is null)
            {
                continue;
            }

            var friendlyName = disk.DisplayName ?? status.DeviceIdentifier ?? disk.Name;
            var baseGroup = GroupPath.From("Disks", friendlyName);

            if (status.TemperatureCelsius is double temp)
            {
                sensors.Add(SensorFactory.Create(
                    $"disk.{disk.Name}.temp",
                    GroupPath.From(baseGroup.Concat(new[] { "Temperature" })),
                    $"{friendlyName} Temp",
                    SensorType.DiskTemperature,
                    temp,
                    "C",
                    Name));
            }

            if (status.IsHealthy is bool healthy)
            {
                sensors.Add(SensorFactory.Create(
                    $"disk.{disk.Name}.health",
                    GroupPath.From(baseGroup.Concat(new[] { "Health" })),
                    $"{friendlyName} SMART",
                    SensorType.DiskHealth,
                    healthy ? 1 : 0,
                    string.Empty,
                    Name,
                    textValue: healthy ? "OK" : "Attention"));
            }
        }

        return sensors;
    }

    private async Task<IReadOnlyList<BlockDevice>> GetDisksAsync(CancellationToken cancellationToken)
    {
        var result = await _processRunner.RunAsync("lsblk", "-J -o NAME,TYPE,MODEL,VENDOR,SERIAL", cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return Array.Empty<BlockDevice>();
        }

        try
        {
            using var document = JsonDocument.Parse(result.StandardOutput);
            if (!document.RootElement.TryGetProperty("blockdevices", out var devicesElement) ||
                devicesElement.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<BlockDevice>();
            }

            var devices = new List<BlockDevice>();
            foreach (var element in devicesElement.EnumerateArray())
            {
                if (!element.TryGetProperty("type", out var typeProp) ||
                    typeProp.ValueKind != JsonValueKind.String ||
                    !typeProp.GetString()!.Equals("disk", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!element.TryGetProperty("name", out var nameProp) ||
                    nameProp.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var name = nameProp.GetString()!;
                var model = element.TryGetProperty("model", out var modelProp) ? modelProp.GetString() : null;
                var vendor = element.TryGetProperty("vendor", out var vendorProp) ? vendorProp.GetString() : null;
                var serial = element.TryGetProperty("serial", out var serialProp) ? serialProp.GetString() : null;

                var labelParts = new[]
                {
                    vendor,
                    model,
                    string.IsNullOrWhiteSpace(serial) ? null : $"SN:{serial}"
                }.Where(part => !string.IsNullOrWhiteSpace(part));

                var displayName = labelParts.Any()
                    ? string.Join(" ", labelParts)
                    : name.ToUpperInvariant();

                devices.Add(new BlockDevice(name, displayName));
            }

            return devices;
        }
        catch (JsonException)
        {
            return Array.Empty<BlockDevice>();
        }
    }

    private sealed record BlockDevice(string Name, string? DisplayName);
}
