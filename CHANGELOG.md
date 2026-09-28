# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.0] - 2026-09-28

First public release.

### Added
- `--once` one-shot JSON report of every physical disk: model, bus, media type, capacity, drive
  letters, Windows health status, temperature, wear, power-on hours, read/write error totals.
- Loopback HTTP API (`/probe/health`, `/probe/smart`) on port 5199 by default; `--port` and
  `--bind any` to change it.
- `GET /` returns a JSON index of the available routes; in Development it serves a self-contained
  page that calls each endpoint and pretty-prints the response.
- `--install` / `--uninstall` to register the probe as an auto-starting, self-restarting
  `LocalSystem` Windows service via `sc.exe`, with the arguments fixed at install time.
- Two temperature sources — `IOCTL_STORAGE_QUERY_PROPERTY` first, `MSFT_StorageReliabilityCounter`
  as fallback — with `temperatureSource` in the output recording which one answered.
- Honest failure reporting: nullable sensor fields and an `unavailable` reason per disk.
- Native AOT: publishes to one ~10 MB native executable per architecture (`win-x64`, `win-arm64`)
  with no runtime to install.
- Single-instance guard scoped per port, so a development copy can run beside the installed
  service.
- Unit tests for descriptor parsing, command-line parsing and code-to-name mapping; WMI smoke
  tests.

### Changed
- Targets .NET 10 (LTS).
- WMI is reached through a hand-bound COM client (`Interop/Wmi.cs`) instead of `System.Management`,
  which cannot be compiled ahead of time. Output is unchanged.
- One package dependency remains (`Microsoft.Extensions.Hosting.WindowsServices`); the command
  line is parsed in-house and JSON is source-generated.
- The probe no longer re-launches itself elevated. An unelevated run reports what it could not read
  and says so on stderr; the service install is the supported path to privilege.

[Unreleased]: https://github.com/rpernett/SMARTProbe/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/rpernett/SMARTProbe/releases/tag/v1.0.0
