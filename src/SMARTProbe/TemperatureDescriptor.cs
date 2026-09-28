// SPDX-License-Identifier: MIT

using System.Buffers.Binary;

namespace SmartProbe;

/// <summary>
/// Parsed STORAGE_TEMPERATURE_DATA_DESCRIPTOR, as returned by
/// IOCTL_STORAGE_QUERY_PROPERTY / StorageDeviceTemperatureProperty.
/// </summary>
/// <param name="Current">Hottest sensor reading in °C.</param>
/// <param name="Critical">Drive's critical trip point in °C, if it reports one.</param>
/// <param name="Warning">Drive's warning trip point in °C, if it reports one.</param>
internal readonly record struct TemperatureDescriptor(int Current, int? Critical, int? Warning)
{
    // STORAGE_TEMPERATURE_DATA_DESCRIPTOR layout (all little-endian):
    //   0  ULONG  Version
    //   4  ULONG  Size
    //   8  SHORT  CriticalTemperature
    //  10  SHORT  WarningTemperature
    //  12  USHORT InfoCount
    //  14  UCHAR  Reserved0[2]
    //  16  ULONG  Reserved1[2]
    //  24  STORAGE_TEMPERATURE_INFO TemperatureInfo[]
    //
    // STORAGE_TEMPERATURE_INFO (16 bytes):
    //   0  USHORT Index
    //   2  SHORT  Temperature
    //   4  SHORT  OverThreshold
    //   6  SHORT  UnderThreshold
    //   8  BOOLEAN OverThresholdChangable, UnderThresholdChangable, EventGenerated, Reserved0
    //  12  ULONG  Reserved1
    internal const int HeaderSize = 24;
    internal const int InfoSize = 16;

    private const int CriticalOffset = 8;
    private const int WarningOffset = 10;
    private const int InfoCountOffset = 12;
    private const int InfoTemperatureOffset = 2;

    /// <summary>
    /// Parses the descriptor. Returns false when the buffer is too short or no sensor reports a
    /// plausible temperature — silence is treated the same as a hardware refusal.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> buffer, out TemperatureDescriptor descriptor)
    {
        descriptor = default;

        if (buffer.Length < HeaderSize)
        {
            return false;
        }

        var critical = BinaryPrimitives.ReadInt16LittleEndian(buffer[CriticalOffset..]);
        var warning = BinaryPrimitives.ReadInt16LittleEndian(buffer[WarningOffset..]);
        var count = BinaryPrimitives.ReadUInt16LittleEndian(buffer[InfoCountOffset..]);

        int? hottest = null;
        for (var i = 0; i < count; i++)
        {
            var offset = HeaderSize + (i * InfoSize) + InfoTemperatureOffset;
            if (offset + sizeof(short) > buffer.Length)
            {
                break;
            }

            var celsius = BinaryPrimitives.ReadInt16LittleEndian(buffer[offset..]);
            if (IsPlausible(celsius) && (hottest == null || celsius > hottest))
            {
                hottest = celsius;
            }
        }

        if (hottest == null)
        {
            return false;
        }

        descriptor = new TemperatureDescriptor(hottest.Value, Sane(critical), Sane(warning));
        return true;
    }

    /// <summary>No drive runs at 0 °C or above 150 °C; anything outside that is "not supplied".</summary>
    internal static bool IsPlausible(int celsius) => celsius is > 0 and < 150;

    private static int? Sane(short value) => IsPlausible(value) ? value : null;
}
