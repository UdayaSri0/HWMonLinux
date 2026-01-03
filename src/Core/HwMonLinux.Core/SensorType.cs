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
    MemoryUsed,
    FanSpeed,
    Voltage,
    Power,
    Current,
    Energy,
    DiskHealth,
    DiskTemperature,
    DiskUsage,
    GpuTemperature,
    GpuLoad,
    GpuMemoryUsage,
    GpuFanSpeed,
    BatteryPercentage,
    BatteryTemperature,
    BatteryCurrent,
    BatteryVoltage
}
