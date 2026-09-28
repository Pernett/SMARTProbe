# Contributing to SMARTProbe

Thanks for your interest. SMARTProbe is small on purpose; the most valuable contributions are
hardware reports, bug fixes, and careful additions that keep it small.

## Ground rules

- Be kind. See the [Code of Conduct](CODE_OF_CONDUCT.md).
- Open an issue before a large change so the design can be discussed first.
- Keep the probe **stateless** and **loopback-by-default**. Anything that persists data, schedules
  work, or opens the network by default belongs in a caller, not here.
- Keep it **dependency-light and AOT-clean**. There is one NuGet package today. A new one needs a
  reason a few hundred lines of code cannot provide, and it must be Native AOT compatible — the
  build fails on trim/AOT analyzer warnings.
- Every sensor field stays nullable, and every failure to read stays visible in the output.
  Silent zeros are bugs.

## Development setup

You need Windows and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
(`global.json` pins the feature band). Building and testing need nothing else:

```powershell
git clone https://github.com/rpernett/SMARTProbe.git
cd SMARTProbe
dotnet build
dotnet test
```

To run the probe from source:

```powershell
# One-shot JSON report (elevate the prompt to get the counters)
dotnet run --project src/SMARTProbe -- --once

# Web API; open http://127.0.0.1:5199/ for the development test page
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project src/SMARTProbe
```

The development page at `/` has a button per endpoint and pretty-prints the response with its
status and timing. It is embedded in the binary (`DevPage.cs`), has no external assets, and is
mapped only in Development, so it never ships in a service install. In other environments `/`
returns a JSON index of routes.

### Publishing the native executable

Releases are Native AOT. Publishing needs the MSVC linker: install the **Desktop development with
C++** workload in Visual Studio 2022 or Build Tools 2022 (plus **C++ ARM64 build tools** for
`win-arm64`).

```powershell
dotnet publish src/SMARTProbe -c Release -r win-x64 -o artifacts/win-x64
```

Compile-to-native takes 10–30 s. `dotnet build` does not compile natively; it only runs the AOT and
trim analyzers, so a plain build stays fast.

> If you also have a **Visual Studio Insiders/preview** installed, the .NET SDK's toolchain
> discovery prefers it, and some preview `vcvarsall.bat` versions fail to find `vswhere.exe`,
> producing a garbled linker error. Put the VS Installer folder on `PATH` for the publish:
> `$env:PATH = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer;$env:PATH"`.

## Code style

`.editorconfig` defines it and the build enforces it (`TreatWarningsAsErrors` with the
`latest-recommended` analyzer set). If the analyzers flag something you believe is wrong for this
codebase, disable that one rule in `.editorconfig` with a comment explaining why, in its own commit.

In short: Allman braces, `var`, file-scoped namespaces, four-space indent, comments that explain
*why* rather than *what*. Win32 names keep their documented spelling (`STORAGE_PROPERTY_QUERY`).

## Interop

`Interop/Wmi.cs` talks to WMI through raw COM vtables (`delegate* unmanaged`) so that Native AOT can
compile it; `System.Management` cannot be used. If you extend it:

- Vtable slot numbers are documented per interface in comments. Check them against `wbemcli.h`.
- Every `BSTR` you allocate is freed in a `finally`; every `VARIANT` you receive is cleared.
- CIM `uint64` values arrive as `VT_BSTR` strings — `WmiObject.GetInteger` already handles this.
- COM objects must be released on the thread that created them; `WmiConnection` is not shared.

P/Invokes use `[LibraryImport]` (source-generated), never `[DllImport]`.

## Tests

- Pure logic (descriptor parsing, argument parsing, code-to-name maps) has unit tests in
  `tests/SMARTProbe.Tests`. New pure logic should come with them.
- `DriveSmartReaderTests` are smoke tests against the live WMI storage namespace. They pass both
  elevated and unelevated; an unelevated run must produce a well-formed report that *says* it was
  unelevated.
- CI runs on `windows-latest`. GitHub's Windows runners execute as administrator, so counters are
  read for real there (against a virtual disk), and the AOT publish is smoke-tested with `--once`.

## Hardware reports

Hardware variety is the thing a single maintainer cannot test. If SMARTProbe reports something odd
or unexpected on your machine — a missing value, a `temperatureSource` of `ioctl`, a drive that never
shows counters even when elevated — please open a
[hardware report](https://github.com/rpernett/SMARTProbe/issues/new?template=hardware_report.yml)
with the `--once` output (elevated) and the drive model. Those reports drive the roadmap.

## Pull requests

1. Fork, branch from `main`, make your change.
2. `dotnet build` and `dotnet test` must pass with zero warnings.
3. Add a line under **Unreleased** in [CHANGELOG.md](CHANGELOG.md).
4. Keep the PR focused. Formatting-only changes and dependency bumps go in their own PRs.
5. Describe *why* in the PR body, and how you tested it (what hardware, elevated or not).

Commits are squash-merged, so commit granularity within a PR is up to you.

## Releasing (maintainers)

Tag `vX.Y.Z` on `main`. The release workflow publishes `win-x64` and `win-arm64` native executables
with `-p:Version=X.Y.Z`, zips each with the licence and README alongside a SHA-256 file, keeps the
`.pdb` symbols as a build artifact, and creates a GitHub Release with notes taken from the changelog
section for that version. Move the **Unreleased** entries under the new version heading in
`CHANGELOG.md` before tagging.

## License

By contributing you agree that your contributions are licensed under the [MIT License](LICENSE).
