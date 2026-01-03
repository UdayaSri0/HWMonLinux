## HWMon Linux

HWMon Linux is an open-source hardware monitoring desktop app for Debian-based distributions (Ubuntu, Linux Mint, etc.). It exposes live CPU, memory, fan, disk, and GPU telemetry in a clean Avalonia UI and does **not** require root for the most common sensors.

### Features

- **Rich provider stack**: JSON `lm-sensors` data (temps/fans/voltages/power), cpu freq from `/sys/devices/system/cpu`, `/proc/stat` CPU load, `/proc/meminfo` RAM, SMART health/temps via `lsblk+smartctl`, NVIDIA `nvidia-smi`, GPU sysfs fallback (AMD/Intel), laptop battery telemetry, and sysfs `hwmon` fallback.
- **Live dashboard**: summary cards (CPU temp, CPU load, RAM usage), adjustable polling interval, warning banner for degraded providers.
- **Tree view explorer**: TreeDataGrid groups sensors (CPU/Temperatures, GPU/NVIDIA, Disks/NVMe0, Power/BAT0, …) with Value/Min/Max columns and instant search filtering; Min/Max tracked over time with a reset button.
- **Branding & help**: Help → About dialog highlights author (Udaya Sri), repo link, license, and version.
- **Resilience**: Providers auto-skip when binaries/files are missing; every reading carries timestamps and metadata for logging/alerts later.
- **CI/tests**: Parser unit tests plus GitHub Actions build/test on Ubuntu.

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
  - `sudo apt install lm-sensors` – enables the JSON `sensors -j` provider (run `sudo sensors-detect` once, then `sudo service kmod start` if needed)
  - `sudo apt install smartmontools` – exposes SMART health/temp via `smartctl -j -a`
  - `nvidia-smi` binary (bundled with proprietary NVIDIA driver) for rich GPU stats
  - For laptop telemetry: ensure `/sys/class/power_supply/BAT*` exists (standard on Linux Mint/Ubuntu)

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
