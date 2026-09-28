// SPDX-License-Identifier: MIT

namespace SmartProbe;

/// <summary>
/// Human-readable names for the enumerations MSFT_PhysicalDisk reports as integers.
/// Values follow the Windows Storage Management API documentation.
/// </summary>
internal static class StorageNames
{
    public static string MediaType(long? value) => value switch
    {
        3 => "HDD",
        4 => "SSD",
        5 => "SCM",
        _ => "Unspecified"
    };

    public static string BusType(long? value) => value switch
    {
        1 => "SCSI",
        2 => "ATAPI",
        3 => "ATA",
        4 => "IEEE 1394",
        5 => "SSA",
        6 => "Fibre Channel",
        7 => "USB",
        8 => "RAID",
        9 => "iSCSI",
        10 => "SAS",
        11 => "SATA",
        12 => "SD",
        13 => "MMC",
        15 => "Virtual",
        16 => "File Backed Virtual",
        17 => "NVMe",
        18 => "SCM",
        19 => "UFS",
        _ => "Unknown"
    };

    public static string HealthStatus(long? value) => value switch
    {
        0 => "Healthy",
        1 => "Warning",
        2 => "Unhealthy",
        _ => "Unknown"
    };

    /// <summary>
    /// MSFT_Partition.DriveLetter is a CIM char16: the UTF-16 code unit of the letter, or 0 when the
    /// partition has none. Returns "C:" style, or null.
    /// </summary>
    public static string? DriveLetter(long? codeUnit) =>
        codeUnit is >= 'A' and <= 'z' && char.IsAsciiLetter((char)codeUnit.Value)
            ? $"{char.ToUpperInvariant((char)codeUnit.Value)}:"
            : null;
}
