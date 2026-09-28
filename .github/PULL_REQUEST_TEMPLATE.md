## What

<!-- One or two sentences: what changes and why. Link the issue if there is one. -->

## How it was tested

<!-- Hardware (drive models / bus types), elevated or not, `dotnet test` result. -->

## Checklist

- [ ] `dotnet build` and `dotnet test` pass with zero warnings
- [ ] Sensor fields stay nullable and failures stay visible in the output
- [ ] No new default network exposure (loopback stays the default)
- [ ] Added a line under **Unreleased** in `CHANGELOG.md`
