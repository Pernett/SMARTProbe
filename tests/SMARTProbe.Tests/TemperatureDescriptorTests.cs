// SPDX-License-Identifier: MIT

using System.Buffers.Binary;

namespace SmartProbe.Tests;

public class TemperatureDescriptorTests
{
    /// <summary>Builds a STORAGE_TEMPERATURE_DATA_DESCRIPTOR with the given sensor readings.</summary>
    private static byte[] Descriptor(short critical, short warning, params short[] sensors)
    {
        var buffer = new byte[TemperatureDescriptor.HeaderSize + (sensors.Length * TemperatureDescriptor.InfoSize)];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0), 1);                     // Version
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4), (uint)buffer.Length);   // Size
        BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(8), critical);
        BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(10), warning);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(12), (ushort)sensors.Length);

        for (var i = 0; i < sensors.Length; i++)
        {
            var info = TemperatureDescriptor.HeaderSize + (i * TemperatureDescriptor.InfoSize);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(info), (ushort)i);      // Index
            BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(info + 2), sensors[i]);  // Temperature
        }

        return buffer;
    }

    [Fact]
    public void Parses_single_sensor_with_trip_points()
    {
        var ok = TemperatureDescriptor.TryParse(Descriptor(critical: 85, warning: 70, 41), out var d);

        Assert.True(ok);
        Assert.Equal(41, d.Current);
        Assert.Equal(85, d.Critical);
        Assert.Equal(70, d.Warning);
    }

    [Fact]
    public void Reports_the_hottest_sensor()
    {
        var ok = TemperatureDescriptor.TryParse(Descriptor(0, 0, 38, 52, 44), out var d);

        Assert.True(ok);
        Assert.Equal(52, d.Current);
    }

    [Fact]
    public void Implausible_trip_points_become_null()
    {
        // 0 means "not supplied"; -1 and 200 are noise from drivers that leave the field unset.
        Assert.True(TemperatureDescriptor.TryParse(Descriptor(0, -1, 40), out var d1));
        Assert.Null(d1.Critical);
        Assert.Null(d1.Warning);

        Assert.True(TemperatureDescriptor.TryParse(Descriptor(200, 150, 40), out var d2));
        Assert.Null(d2.Critical);
        Assert.Null(d2.Warning);
    }

    [Fact]
    public void Ignores_sensors_with_implausible_readings()
    {
        // 0 °C and 150+ °C are "not supplied"; only the 39 counts.
        Assert.True(TemperatureDescriptor.TryParse(Descriptor(0, 0, 0, 39, 150, -5), out var d));
        Assert.Equal(39, d.Current);
    }

    [Fact]
    public void Fails_when_no_sensor_is_plausible()
    {
        Assert.False(TemperatureDescriptor.TryParse(Descriptor(0, 0, 0, 0), out _));
        Assert.False(TemperatureDescriptor.TryParse(Descriptor(0, 0), out _));
    }

    [Fact]
    public void Fails_on_short_buffer()
    {
        Assert.False(TemperatureDescriptor.TryParse(new byte[TemperatureDescriptor.HeaderSize - 1], out _));
        Assert.False(TemperatureDescriptor.TryParse([], out _));
    }

    [Fact]
    public void Does_not_read_past_a_truncated_sensor_array()
    {
        // InfoCount claims three sensors but the buffer holds only one — the parser must stop.
        var full = Descriptor(0, 0, 45, 60, 70);
        var truncated = full[..(TemperatureDescriptor.HeaderSize + TemperatureDescriptor.InfoSize)];

        Assert.True(TemperatureDescriptor.TryParse(truncated, out var d));
        Assert.Equal(45, d.Current);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(45, true)]
    [InlineData(149, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(150, false)]
    public void Plausibility_window_is_exclusive_of_0_and_150(int celsius, bool expected)
    {
        Assert.Equal(expected, TemperatureDescriptor.IsPlausible(celsius));
    }
}
