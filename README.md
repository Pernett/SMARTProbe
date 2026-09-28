# SMARTProbe

[![CI](https://github.com/rpernett/SMARTProbe/actions/workflows/ci.yml/badge.svg)](https://github.com/rpernett/SMARTProbe/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
![Platform: Windows](https://img.shields.io/badge/platform-Windows-0078D6)
![Native AOT](https://img.shields.io/badge/.NET%2010-Native%20AOT-512BD4)

A small, stateless Windows tool that answers one question: **how are my drives doing?**
It reads SMART reliability counters (temperature, wear, power-on hours, error totals) for every
physical disk and hands them back as JSON — either once on the command line, or continuously over a
tiny loopback HTTP API.

Reading SMART on Windows needs administrator rights. SMARTProbe exists so that a program *without*
those rights can still get the answer: install it once (elevated) as a `LocalSystem` service and it
answers on `http://127.0.0.1:5199` forever, with no further UAC prompts.

It ships as **one native executable of about 10 MB** with no runtime to install and no other files.

```json
{
  "timestampUtc": "2026-09-28T22:38:10Z",
  "machineName": "WORKSTATION",
  "elevated": true,
  "disks": [
    {
      "deviceId": "1",
      "friendlyName": "Samsung SSD 990 EVO Plus 4TB",
      "driveLetters": "C:",
      "mediaType": "SSD",
      "busType": "NVMe",
      "healthStatus": "Healthy",
      "sizeBytes": 4000787030016,
      "temperatureC": 41,
      "temperatureMaxC": 58,
      "wearPercent": 2,
      "powerOnHours": 3117,
      "readErrorsTotal": 0,
      "writeErrorsTotal": 0,
      "temperatureSource": "wmi"
    }
  ]
}
```

## Design principles

- **Stateless.** SMARTProbe never persists anything. Scheduling, history and alerting belong to
  whoever calls it. Losing the probe costs nothing but the answer.
- **Honest.** Every sensor field is nullable. If a value could not be read, it is omitted and
  `unavailable` says why (`"Requires administrator rights"`, `"Counters unavailable"`).
  `temperatureSource` records which mechanism supplied the temperature so a real reading can never
  be confused with a silent hardware refusal.
- **Loopback by default.** The probe hands out hardware detail. Exposing it beyond the local machine
  is an explicit `--bind any`, never a default. See [SECURITY.md](SECURITY.md).
- **Privilege spent once.** The service is registered with its arguments baked into the binary path,
  so a caller can never talk it into running something else.
- **Almost no dependencies.** One NuGet package (Windows service hosting). WMI is reached through
  hand-bound COM vtables, the command line is parsed in-house, JSON is source-generated. That is
  what makes the Native AOT build possible — and what keeps the supply chain short.

## Requirements

- Windows 10 / 11 or Windows Server 2016+, x64 or ARM64. Nothing to install: the executable is
  fully native.
- Administrator rights to read SMART counters (or the service install, which does this for you).

## Install

Download `SMARTProbe-<version>-win-x64.zip` (or `-win-arm64`) from the
[latest release](https://github.com/rpernett/SMARTProbe/releases/latest), verify the `.sha256`,
extract `SMARTProbe.exe` somewhere permanent (for example `C:\Program Files\SMARTProbe\`), then from
an **elevated** prompt:

```powershell
SMARTProbe.exe --install
```

That registers and starts a Windows service named `SMARTProbe` running as `LocalSystem`, listening on
`http://127.0.0.1:5199`, set to start automatically and to restart itself on failure.

To remove it:

```powershell
SMARTProbe.exe --uninstall
```

## Usage

### One-shot report

```powershell
SMARTProbe.exe --once
```

Prints one report as JSON to stdout and exits. Pipe it wherever you like:

```powershell
SMARTProbe.exe --once | ConvertFrom-Json | Select-Object -ExpandProperty disks | Format-Table friendlyName, temperatureC, wearPercent, healthStatus
```

Run it from an elevated prompt to get the counters; unelevated, it still returns every disk with
`"unavailable": "Requires administrator rights"` and warns on stderr. It never triggers a UAC prompt
on its own.

### HTTP API

Running with no flags (or as the installed service) starts the web API.

| Route | Returns |
|---|---|
| `GET /` | Name, version and the list of routes below. In Development, a page to exercise the API from. |
| `GET /probe/health` | `status`, `machine`, `elevated`, `service`, `version`, `timestampUtc` |
| `GET /probe/smart` | The full report shown above |

```powershell
Invoke-RestMethod http://127.0.0.1:5199/probe/smart
```

There is no authentication. Read [SECURITY.md](SECURITY.md) before using `--bind any`.

### Command line

```
Usage:
  SMARTProbe [options]

Options:
  -p, --port <port>              TCP port for the web service. [default: 5199]
  -b, --bind <loopback|any>      Interface to bind: 'loopback' (default, local callers only) or 'any'.
  -1, --once                     Print one SMART report as JSON to stdout and exit.
  --install                      Install as a LocalSystem Windows service and start it. Requires elevation.
  --uninstall                    Stop and remove the Windows service. Requires elevation.
  --service-name <name>          Service name to install or remove. [default: SMARTProbe]
  -h, --help                     Show this help.
  --version                      Show the version.
```

`--port` and `--bind` given alongside `--install` are baked into the service definition.

## Report fields

| Field | Type | Meaning |
|---|---|---|
| `deviceId` | string | Windows disk index (`0`, `1`, …), matches `\\.\PhysicalDriveN` |
| `friendlyName` | string | Model string as reported by the drive |
| `driveLetters` | string? | Mounted volumes on this disk, e.g. `"C:, D:"` |
| `mediaType` | string | `HDD`, `SSD`, `SCM`, `Unspecified` |
| `busType` | string | `SATA`, `NVMe`, `USB`, `RAID`, `Virtual`, … |
| `healthStatus` | string | Windows' own verdict: `Healthy`, `Warning`, `Unhealthy`, `Unknown` |
| `sizeBytes` | long | Capacity |
| `temperatureC` | int? | Current temperature |
| `temperatureMaxC` | int? | Highest temperature the drive has recorded (WMI only) |
| `criticalTemperatureC` / `warningTemperatureC` | int? | Drive's own trip points (IOCTL only) |
| `wearPercent` | int? | Wear indicator, 0–100, SSDs only |
| `powerOnHours` | ulong? | Lifetime power-on hours |
| `readErrorsTotal` / `writeErrorsTotal` | ulong? | Lifetime uncorrected error counts |
| `temperatureSource` | string? | `"ioctl"`, `"wmi"`, or absent when no temperature was read |
| `unavailable` | string? | Present only when a read failed; says why |

## How it reads SMART (and what was learned)

Two mechanisms are tried, because neither alone is enough:

1. **`IOCTL_STORAGE_QUERY_PROPERTY` / `StorageDeviceTemperatureProperty`** — pure kernel32, sub-millisecond,
   and the only route to the drive's own critical/warning trip points. In practice most consumer storage
   drivers do not implement it: measured on a Dell Precision 3680 (Toshiba SATA HDD + Samsung 990 EVO NVMe)
   it returned `ERROR_INVALID_FUNCTION` even from an elevated `LocalSystem` process holding a read/write
   handle. **Privilege is not what it wants** — the property is simply unimplemented. It is kept as a
   cheap fast path for hardware that does support it.
2. **`MSFT_StorageReliabilityCounter`** (WMI, `root\Microsoft\Windows\Storage`) — roughly 150–300 ms,
   requires administrator, and is the one that actually answers. It has supplied every reading observed
   so far on both SATA and NVMe, and it is the only source of wear, power-on hours and error totals.

Temperature tries the IOCTL first and falls back to WMI. A reported `0` is treated as "not supplied"
(no drive runs at 0 °C), as is anything at or above 150 °C.

WMI is called through its COM interfaces directly (`src/SMARTProbe/Interop/Wmi.cs`) rather than via
`System.Management`, which depends on runtime COM interop that Native AOT cannot compile. The binding
covers exactly what the probe needs — connect, query, read a property — in about 300 lines.

If your hardware reports `"temperatureSource": "ioctl"`, please [open an issue](https://github.com/rpernett/SMARTProbe/issues/new?template=hardware_report.yml)
and say what drive it is — that data point is useful.

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). Building and testing
need nothing else:

```powershell
dotnet build
dotnet test
```

Publishing the native executable additionally needs the MSVC linker — install the
**Desktop development with C++** workload in Visual Studio or Build Tools (add **C++ ARM64 build
tools** for `win-arm64`):

```powershell
dotnet publish src/SMARTProbe -c Release -r win-x64 -o artifacts/win-x64
```

The output is `SMARTProbe.exe` plus a `.pdb` you can keep for crash symbols. See
[CONTRIBUTING.md](CONTRIBUTING.md) for the development workflow.

## Roadmap

- Per-sensor temperatures where the IOCTL path reports more than one.
- Direct SMART/NVMe log reads (`SMART_RCV_DRIVE_DATA`, NVMe health log page 02h) as a richer,
  faster source beside WMI — the same route CrystalDiskInfo and smartmontools take.
- Linux support via `/sys/class/nvme` and `smartctl` is out of scope for this project; the Windows
  privilege problem is what it exists to solve.

## License

[MIT](LICENSE) © 2026 Robert Pernett
