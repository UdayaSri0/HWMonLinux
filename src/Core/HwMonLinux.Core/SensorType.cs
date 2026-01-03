namespace HwMonLinux.Core;

/// <summary>
/// Describes the type of a collected sensor reading. This makes it
/// straightforward for the UI to group and style sensors.
/// </summary>
public enum SensorType
{
    Unknown = 0,
    CpuTemperature,
    CpuLoad,
    CpuFrequency,
    MemoryUsage,
    MemoryAvailable,
    FanSpeed,
    DiskHealth,
    DiskTemperature,
    DiskUsage,
    GpuTemperature,
    GpuLoad,
    GpuMemoryUsage,
    GpuFanSpeed
}
