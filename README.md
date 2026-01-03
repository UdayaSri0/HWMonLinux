## HWMon Linux

HWMon Linux is an open-source hardware monitoring desktop app for Debian-based distributions (Ubuntu, Linux Mint, etc.). It exposes live CPU, memory, fan, disk, and GPU telemetry in a clean Avalonia UI and does **not** require root for the most common sensors.

### Features

- Live refresh of CPU temperature, CPU load (delta from `/proc/stat`), per-core frequencies, and RAM usage from `/proc/meminfo`.
- Sysfs `hwmon` reader for temperatures and fan RPM with graceful fallbacks when files are missing.
- Optional disk SMART parsing via `smartctl -j` and NVIDIA GPU stats via `nvidia-smi` (auto-detects availability).
- MVVM UI with summary cards + searchable sensor table, and adjustable refresh interval (0.5s – 10s).
- Sensor collector service that aggregates providers asynchronously and streams `Snapshot` updates with warning messages instead of crashing when a provider fails.
- Basic parser unit tests and GitHub Actions workflow building/testing on Ubuntu.

### Architecture

```
HwMonLinux.sln
├── src/Core/HwMonLinux.Core            # SensorReading/Snapshot models, ISensorProvider contract, refresh options
├── src/Providers/HwMonLinux.Providers  # Sysfs/SMART/NVIDIA providers + SensorCollectorService orchestration
├── src/App/HwMonLinux.App              # Avalonia desktop client (MVVM)
└── tests/Providers.Tests               # Parser unit tests
```

- **Core**: Free of platform specifics; defines the models shared by both providers and UI.
- **Providers**: Implements `ISensorProvider` for `/sys/class/hwmon`, `/proc/stat`, `/proc/meminfo`, CPU frequency files, `smartctl`, and `nvidia-smi`. Includes abstractions for process execution and JSON/text parsers plus the `SensorCollectorService` that runs the refresh loop.
- **App**: Avalonia UI that subscribes to the collector service, updates dashboard cards, and surfaces data via `SensorRowViewModel`. The UI never touches sysfs or launches commands directly.

### Requirements

- .NET 8 SDK
- Debian-based distribution with access to `/sys` and `/proc`
- Optional tools for richer data:
  - `sudo apt install smartmontools` for disk SMART
  - `nvidia-smi` (from proprietary NVIDIA driver) for GPU telemetry

### Getting Started

```bash
git clone https://github.com/UdayaSri0/HwMonLinux.git
cd HwMonLinux
dotnet restore
dotnet build
dotnet run --project src/App/HwMonLinux.App
```

The UI starts with a default 2s refresh interval; adjust using the slider in the header. Sensors that cannot be accessed (missing files, permissions, device absent) show “Not available” and an entry appears in the warnings banner instead of failing the loop.

### Tests

Parser tests cover `/proc/stat`, `/proc/meminfo`, and `smartctl -j` JSON parsing. Run them via:

```bash
dotnet test --configuration Release
```

### Troubleshooting

- **No CPU temps/fans**: Ensure your kernel exposes `hwmon` entries (check `/sys/class/hwmon`). Laptop firmware may require `lm-sensors`.
- **SMART data blank**: Install `smartmontools` and make sure your user can run `smartctl -j /dev/sdX` without sudo (use `udev` rules). Otherwise the provider is skipped.
- **NVIDIA stats missing**: The `nvidia-smi` command must exist in `$PATH`. For AMD/Intel GPUs, temperature sensors should still surface via the `hwmon` provider if the driver exposes them.
- **High refresh intervals ignored**: The interval is clamped between 0.5s and 5 minutes to avoid UI overload; values outside the range are rounded to the closest limit.

### Next Steps

- v0.5: extend the disk SMART view and expose per-core cards/fan details.
- v0.7: add GPU providers for AMD ROCm/Intel sysfs, logging (CSV/JSON), and notifications.
- v1.0: Debian packaging, desktop entry, and app icon.
