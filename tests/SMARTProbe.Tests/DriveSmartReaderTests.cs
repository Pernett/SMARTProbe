// SPDX-License-Identifier: MIT

namespace SmartProbe.Tests;

/// <summary>
/// Smoke tests against the real WMI storage namespace. They run on any Windows machine, elevated
/// or not: an unprivileged run must still produce a well-formed report that says so.
/// </summary>
public class DriveSmartReaderTests
{
    [Fact]
    public void Read_returns_a_well_formed_report()
    {
        var before = DateTime.UtcNow;
        var report = DriveSmartReader.Read();

        Assert.Equal(Environment.MachineName, report.MachineName);
        Assert.InRange(report.TimestampUtc, before.AddSeconds(-1), DateTime.UtcNow.AddSeconds(1));
        Assert.Equal(DriveSmartReader.IsElevated(), report.Elevated);
        Assert.NotEmpty(report.Disks);
    }

    [Fact]
    public void Every_disk_has_a_device_id_or_an_explanation()
    {
        foreach (var disk in DriveSmartReader.Read().Disks)
        {
            Assert.True(
                !string.IsNullOrEmpty(disk.DeviceId) || disk.Unavailable != null,
                "a disk with no DeviceId must explain why");
        }
    }

    [Fact]
    public void Temperature_and_source_are_reported_together()
    {
        foreach (var disk in DriveSmartReader.Read().Disks)
        {
            Assert.Equal(disk.TemperatureC.HasValue, disk.TemperatureSource != null);
            if (disk.TemperatureSource != null)
            {
                Assert.True(disk.TemperatureSource is "ioctl" or "wmi", $"unexpected source '{disk.TemperatureSource}'");
            }
        }
    }

    [Fact]
    public void Unprivileged_reads_name_the_reason()
    {
        if (DriveSmartReader.IsElevated())
        {
            return; // Nothing to assert about the unprivileged path when we have privilege.
        }

        foreach (var disk in DriveSmartReader.Read().Disks.Where(d => d.TemperatureC == null))
        {
            Assert.Equal("Requires administrator rights", disk.Unavailable);
        }
    }
}
