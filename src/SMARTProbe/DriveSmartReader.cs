// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using System.Security.Principal;
using SmartProbe.Interop;

namespace SmartProbe;

/// <summary>One physical disk as the probe reports it. Every sensor field is nullable.</summary>
public sealed record DiskSmart
{
    public string DeviceId { get; init; } = string.Empty;
    public string FriendlyName { get; init; } = string.Empty;
    public string? DriveLetters { get; init; }
    public string MediaType { get; init; } = "Unspecified";
    public string BusType { get; init; } = "Unknown";
    public string HealthStatus { get; init; } = "Unknown";
    public long SizeBytes { get; init; }
    public int? TemperatureC { get; init; }
    public int? TemperatureMaxC { get; init; }
    public int? CriticalTemperatureC { get; init; }
    public int? WarningTemperatureC { get; init; }
    public int? WearPercent { get; init; }
    public ulong? PowerOnHours { get; init; }
    public ulong? ReadErrorsTotal { get; init; }
    public ulong? WriteErrorsTotal { get; init; }

    /// <summary>Which source supplied the temperature: "ioctl", "wmi", or null.</summary>
    public string? TemperatureSource { get; init; }

    /// <summary>Populated when a read failed; null on success.</summary>
    public string? Unavailable { get; init; }
}

/// <summary>The probe's whole answer, including how privileged it actually was.</summary>
public sealed record SmartReport
{
    public DateTime TimestampUtc { get; init; }
    public string MachineName { get; init; } = string.Empty;
    public bool Elevated { get; init; }
    public IReadOnlyList<DiskSmart> Disks { get; init; } = [];
}

/// <summary>
/// Reads drive temperature and SMART reliability counters.
///
/// Two sources, because neither alone is sufficient:
///   IOCTL_STORAGE_QUERY_PROPERTY / StorageDeviceTemperatureProperty — sub-millisecond, pure
///     kernel32, and would also give the drive's own critical/warning trip points. In practice
///     it is usually silent: measured on a Dell Precision 3680 (Toshiba SATA HDD + Samsung 990 EVO
///     NVMe) it returned ERROR_INVALID_FUNCTION even from an elevated LocalSystem process with
///     a read/write handle, so privilege is NOT what it wants — those storage drivers simply
///     do not implement the property. Kept as a cheap fast path for hardware that does.
///   MSFT_StorageReliabilityCounter (WMI) — ~300 ms, needs administrator, and carries wear,
///     power-on hours and error totals. This is the one that actually answers: it supplied
///     every reading observed so far, on both SATA and NVMe.
///
/// Temperature tries the IOCTL first and falls back to WMI, recording which answered in
/// TemperatureSource so a caller can tell a real reading from a silent hardware refusal —
/// and so this comment can be checked against reality rather than trusted.
/// </summary>
public static partial class DriveSmartReader
{
    private const string StorageNamespace = @"root\Microsoft\Windows\Storage";

    public static SmartReport Read()
    {
        var disks = new List<DiskSmart>();

        try
        {
            using var wmi = WmiConnection.Connect(StorageNamespace);
            var letters = BuildDriveLetterMap(wmi);
            var counters = ReadCounters(wmi, out var countersUnavailable);

            using var physical = wmi.Query("SELECT * FROM MSFT_PhysicalDisk");
            foreach (var disk in physical)
            {
                disks.Add(ReadDisk(disk, letters, counters, countersUnavailable));
            }
        }
        catch (WmiException ex)
        {
            disks.Add(new DiskSmart { Unavailable = $"Disk enumeration failed: {ex.Message}" });
        }

        return new SmartReport
        {
            TimestampUtc = DateTime.UtcNow,
            MachineName = Environment.MachineName,
            Elevated = IsElevated(),
            Disks = disks
        };
    }

    private readonly record struct Counters(
        int? Temperature, int? TemperatureMax, int? Wear,
        ulong? PowerOnHours, ulong? ReadErrorsTotal, ulong? WriteErrorsTotal);

    private static DiskSmart ReadDisk(WmiObject disk, Dictionary<string, string> driveLetters,
        Dictionary<string, Counters> counters, string? countersUnavailable)
    {
        var deviceId = disk.GetString("DeviceId") ?? string.Empty;
        driveLetters.TryGetValue(deviceId, out var letters);

        int? temperature = null, critical = null, warning = null;
        string? source = null;

        // Preferred: the drive's own temperature descriptor, via the disk index.
        if (int.TryParse(deviceId, out var diskNumber)
            && TryReadTemperatureIoctl(diskNumber, out var ioctl))
        {
            temperature = ioctl.Current;
            critical = ioctl.Critical;
            warning = ioctl.Warning;
            source = "ioctl";
        }

        counters.TryGetValue(deviceId, out var c);
        if (temperature == null && c.Temperature != null)
        {
            temperature = c.Temperature;
            source = "wmi";
        }

        // Nothing at all came back and we are not privileged: name the real reason.
        var unavailable = countersUnavailable;
        if (temperature == null && unavailable == null && !IsElevated())
        {
            unavailable = "Requires administrator rights";
        }

        return new DiskSmart
        {
            DeviceId = deviceId,
            FriendlyName = disk.GetString("FriendlyName") ?? string.Empty,
            DriveLetters = letters,
            MediaType = StorageNames.MediaType(disk.GetInteger("MediaType")),
            BusType = StorageNames.BusType(disk.GetInteger("BusType")),
            HealthStatus = StorageNames.HealthStatus(disk.GetInteger("HealthStatus")),
            SizeBytes = disk.GetInteger("Size") ?? 0,
            TemperatureC = temperature,
            TemperatureMaxC = c.TemperatureMax,
            CriticalTemperatureC = critical,
            WarningTemperatureC = warning,
            WearPercent = c.Wear,
            PowerOnHours = c.PowerOnHours,
            ReadErrorsTotal = c.ReadErrorsTotal,
            WriteErrorsTotal = c.WriteErrorsTotal,
            TemperatureSource = source,
            Unavailable = unavailable
        };
    }

    /// <summary>
    /// One sweep of MSFT_StorageReliabilityCounter, keyed by DeviceId (the same disk index
    /// MSFT_PhysicalDisk uses). Access denied is reported as a reason, not thrown: the disks are
    /// still worth listing without their counters.
    /// </summary>
    private static Dictionary<string, Counters> ReadCounters(WmiConnection wmi, out string? unavailable)
    {
        unavailable = null;
        var map = new Dictionary<string, Counters>();

        try
        {
            using var rows = wmi.Query("SELECT * FROM MSFT_StorageReliabilityCounter");
            foreach (var row in rows)
            {
                var deviceId = row.GetString("DeviceId");
                if (deviceId == null)
                {
                    continue;
                }

                // A reported 0 means "not supplied" — no drive runs at 0 °C.
                map[deviceId] = new Counters(
                    Temperature: NonZero(GetInt(row, "Temperature")),
                    TemperatureMax: NonZero(GetInt(row, "TemperatureMax")),
                    Wear: GetInt(row, "Wear"),
                    PowerOnHours: GetULong(row, "PowerOnHours"),
                    ReadErrorsTotal: GetULong(row, "ReadErrorsTotal"),
                    WriteErrorsTotal: GetULong(row, "WriteErrorsTotal"));
            }
        }
        catch (WmiException ex)
        {
            unavailable = ex.IsAccessDenied ? "Requires administrator rights" : $"Counters unavailable ({ex.Message})";
        }

        return map;
    }

    /// <summary>
    /// Physical disk DeviceId → drive letters, from one MSFT_Partition sweep.
    /// MSFT_PhysicalDisk associates only to StorageNode/StorageSubSystem/StoragePool — never
    /// to MSFT_Disk — so ASSOCIATORS OF is a dead end. DeviceId and DiskNumber are the same
    /// disk index, which is the documented join.
    /// </summary>
    private static Dictionary<string, string> BuildDriveLetterMap(WmiConnection wmi)
    {
        var map = new Dictionary<string, List<string>>();

        try
        {
            using var partitions = wmi.Query("SELECT DiskNumber, DriveLetter FROM MSFT_Partition");
            foreach (var partition in partitions)
            {
                var letter = StorageNames.DriveLetter(partition.GetInteger("DriveLetter"));
                var diskNumber = partition.GetString("DiskNumber");
                if (letter == null || diskNumber == null)
                {
                    continue;
                }

                if (!map.TryGetValue(diskNumber, out var list))
                {
                    map[diskNumber] = list = [];
                }
                list.Add(letter);
            }
        }
        catch (WmiException)
        {
            // Best-effort: disks are still worth reporting without their letters.
        }

        return map.ToDictionary(kv => kv.Key, kv => string.Join(", ", kv.Value));
    }

    // ---- IOCTL temperature ----------------------------------------------------------------

    private static bool TryReadTemperatureIoctl(int diskNumber, out TemperatureDescriptor result)
    {
        result = default;

        // Try the read/write handle first and fall back to zero-access. Neither is reliably
        // enough on its own — see the class comment: on the drives measured so far the
        // property is unimplemented regardless of how the handle was opened.
        var handle = OpenDisk(diskNumber, GENERIC_READ | GENERIC_WRITE);
        if (handle == InvalidHandle)
        {
            handle = OpenDisk(diskNumber, 0);
        }
        if (handle == InvalidHandle)
        {
            return false;
        }

        try
        {
            var query = new STORAGE_PROPERTY_QUERY
            {
                PropertyId = StorageDeviceTemperatureProperty,
                QueryType = PropertyStandardQuery
            };

            Span<byte> buffer = stackalloc byte[TemperatureBufferSize];
            buffer.Clear();
            if (!DeviceIoControl(handle, IoctlStorageQueryProperty, in query, Marshal.SizeOf<STORAGE_PROPERTY_QUERY>(),
                    buffer, buffer.Length, out var bytesReturned, IntPtr.Zero))
            {
                return false;
            }

            return TemperatureDescriptor.TryParse(buffer[..Math.Clamp(bytesReturned, 0, buffer.Length)], out result);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static IntPtr OpenDisk(int diskNumber, uint access) =>
        CreateFileW($@"\\.\PhysicalDrive{diskNumber}", access,
            FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);

    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FileShareReadWrite = 3;
    private const uint OpenExisting = 3;
    private const uint IoctlStorageQueryProperty = 0x2D1400;
    private const int StorageDeviceTemperatureProperty = 22;
    private const int PropertyStandardQuery = 0;
    private const int TemperatureBufferSize = 512;

    private static readonly IntPtr InvalidHandle = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    private struct STORAGE_PROPERTY_QUERY
    {
        public int PropertyId;
        public int QueryType;
        public byte Additional0;
        public byte Additional1;
        public byte Additional2;
        public byte Additional3;
    }

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateFileW(string fileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(IntPtr device, uint controlCode,
        in STORAGE_PROPERTY_QUERY inBuffer, int inBufferSize, Span<byte> outBuffer, int outBufferSize,
        out int bytesReturned, IntPtr overlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);

    // ---- helpers --------------------------------------------------------------------------

    public static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static int? NonZero(int? value) => value is > 0 ? value : null;

    private static int? GetInt(WmiObject o, string property) =>
        o.GetInteger(property) is long v and >= int.MinValue and <= int.MaxValue ? (int)v : null;

    private static ulong? GetULong(WmiObject o, string property) =>
        o.GetInteger(property) is long v and >= 0 ? (ulong)v : null;
}
