using System.Globalization;
using System.Text.RegularExpressions;
using HwMonLinux.Core;

namespace HwMonLinux.Providers.Providers;

public sealed partial class CpuFrequencyProvider : ISensorProvider
{
    private readonly string _cpuRoot;

    public CpuFrequencyProvider(string? cpuRoot = null)
    {
        _cpuRoot = cpuRoot ?? "/sys/devices/system/cpu";
    }

    public string Name => "CPU Frequency";

    public ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken) =>
        new(Directory.Exists(_cpuRoot));

    public async Task<IReadOnlyCollection<SensorReading>> GetSensorReadingsAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_cpuRoot))
        {
            return Array.Empty<SensorReading>();
        }

        var sensors = new List<SensorReading>();
        foreach (var cpuDirectory in Directory.EnumerateDirectories(_cpuRoot, "cpu*"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var directoryName = Path.GetFileName(cpuDirectory);
            if (!CpuDirectoryRegex().IsMatch(directoryName))
            {
                continue;
            }

            var scalingFile = Path.Combine(cpuDirectory, "cpufreq", "scaling_cur_freq");
            if (!File.Exists(scalingFile))
            {
                continue;
            }

            var raw = await File.ReadAllTextAsync(scalingFile, cancellationToken).ConfigureAwait(false);
            if (!double.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var khz))
            {
                continue;
            }

            var mhz = khz / 1000d;
            var sensorId = $"cpu.freq.{directoryName}";
            var friendlyName = $"{directoryName.ToUpperInvariant()} Frequency";

            sensors.Add(new SensorReading(sensorId, friendlyName, SensorType.CpuFrequency, mhz, "MHz", Name));
        }

        return sensors;
    }

    [GeneratedRegex("^cpu\\d+$", RegexOptions.Compiled)]
    private static partial Regex CpuDirectoryRegex();
}
