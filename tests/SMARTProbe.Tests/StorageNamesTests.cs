// SPDX-License-Identifier: MIT

namespace SmartProbe.Tests;

public class StorageNamesTests
{
    [Theory]
    [InlineData(3, "HDD")]
    [InlineData(4, "SSD")]
    [InlineData(5, "SCM")]
    [InlineData(0, "Unspecified")]
    [InlineData(99, "Unspecified")]
    [InlineData(null, "Unspecified")]
    public void MediaType(int? code, string expected) =>
        Assert.Equal(expected, StorageNames.MediaType(code));

    [Theory]
    [InlineData(3, "ATA")]
    [InlineData(7, "USB")]
    [InlineData(11, "SATA")]
    [InlineData(17, "NVMe")]
    [InlineData(15, "Virtual")]
    [InlineData(0, "Unknown")]
    [InlineData(null, "Unknown")]
    public void BusType(int? code, string expected) =>
        Assert.Equal(expected, StorageNames.BusType(code));

    [Theory]
    [InlineData(0, "Healthy")]
    [InlineData(1, "Warning")]
    [InlineData(2, "Unhealthy")]
    [InlineData(5, "Unknown")]
    [InlineData(null, "Unknown")]
    public void HealthStatus(int? code, string expected) =>
        Assert.Equal(expected, StorageNames.HealthStatus(code));

    [Fact]
    public void DriveLetter_maps_a_char16_code_unit_to_a_letter()
    {
        Assert.Equal("C:", StorageNames.DriveLetter('C'));
        Assert.Equal("Z:", StorageNames.DriveLetter('Z'));
        Assert.Equal("D:", StorageNames.DriveLetter('d')); // normalised to upper case
    }

    [Fact]
    public void DriveLetter_treats_non_letters_as_no_letter()
    {
        Assert.Null(StorageNames.DriveLetter(null));
        Assert.Null(StorageNames.DriveLetter(0));
        Assert.Null(StorageNames.DriveLetter(' '));
        Assert.Null(StorageNames.DriveLetter('7'));
        Assert.Null(StorageNames.DriveLetter('['));   // between 'Z' and 'a'
        Assert.Null(StorageNames.DriveLetter(0x10000));
        Assert.Null(StorageNames.DriveLetter(-1));
    }
}
