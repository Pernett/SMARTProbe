// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Runtime.InteropServices;

namespace SmartProbe.Interop;

/// <summary>A WMI call failed. <see cref="Exception.HResult"/> carries the COM error code.</summary>
internal sealed class WmiException : Exception
{
    private const int WBEM_E_ACCESS_DENIED = unchecked((int)0x80041003);
    private const int E_ACCESSDENIED = unchecked((int)0x80070005);

    public WmiException(string context, int hresult)
        : base($"{context} failed ({Describe(hresult)})")
    {
        HResult = hresult;
    }

    public bool IsAccessDenied => HResult is WBEM_E_ACCESS_DENIED or E_ACCESSDENIED;

    private static string Describe(int hresult) => hresult switch
    {
        WBEM_E_ACCESS_DENIED or E_ACCESSDENIED => "access denied",
        unchecked((int)0x8004100E) => "invalid namespace",
        unchecked((int)0x80041010) => "invalid class",
        unchecked((int)0x80041017) => "invalid query",
        unchecked((int)0x80041001) => "provider failure",
        _ => $"0x{hresult:X8}"
    };
}

/// <summary>
/// A connection to one WMI namespace, talking to the WMI COM interfaces directly through their
/// vtables. This replaces System.Management, which relies on built-in COM interop that Native AOT
/// cannot compile. Only what the probe needs is bound: connect, query, read properties.
///
/// Every instance must be disposed on the thread that created it (COM apartment rules).
/// </summary>
internal sealed unsafe class WmiConnection : IDisposable
{
    private const uint COINIT_MULTITHREADED = 0x0;
    private const uint CLSCTX_INPROC_SERVER = 0x1;
    private const int WBEM_FLAG_RETURN_IMMEDIATELY = 0x10;
    private const int WBEM_FLAG_FORWARD_ONLY = 0x20;
    private const int WBEM_INFINITE = -1;
    private const int RPC_E_CHANGED_MODE = unchecked((int)0x80010106);

    // Authentication constants for CoSetProxyBlanket — the standard local WMI recipe.
    private const uint RPC_C_AUTHN_WINNT = 10;
    private const uint RPC_C_AUTHZ_NONE = 0;
    private const uint RPC_C_AUTHN_LEVEL_CALL = 3;
    private const uint RPC_C_IMP_LEVEL_IMPERSONATE = 3;
    private const uint EOAC_NONE = 0;

    private static readonly Guid ClsidWbemLocator = new("4590F811-1D3A-11D0-891F-00AA004B2E24");
    private static readonly Guid IidIWbemLocator = new("DC12A687-737F-11CF-884D-00AA004B2E24");

    private void* _services;
    private readonly bool _uninitializeCom;

    private WmiConnection(void* services, bool uninitializeCom)
    {
        _services = services;
        _uninitializeCom = uninitializeCom;
    }

    /// <summary>Connects to a namespace such as <c>root\Microsoft\Windows\Storage</c>.</summary>
    public static WmiConnection Connect(string @namespace)
    {
        // S_OK or S_FALSE (already initialised on this thread) both need a matching CoUninitialize;
        // RPC_E_CHANGED_MODE means another apartment model owns the thread and we leave it alone.
        var init = Ole32.CoInitializeEx(null, COINIT_MULTITHREADED);
        if (init < 0 && init != RPC_E_CHANGED_MODE)
        {
            throw new WmiException("CoInitializeEx", init);
        }
        var uninitialize = init >= 0;

        void* locator = null;
        void* services = null;
        try
        {
            var clsid = ClsidWbemLocator;
            var iid = IidIWbemLocator;
            Check(Ole32.CoCreateInstance(&clsid, null, CLSCTX_INPROC_SERVER, &iid, &locator), "CoCreateInstance(WbemLocator)");

            // IWbemLocator vtable: IUnknown[0..2], ConnectServer[3].
            var connectServer = (delegate* unmanaged<void*, IntPtr, IntPtr, IntPtr, IntPtr, int, IntPtr, void*, void**, int>)VTable(locator)[3];
            var path = Marshal.StringToBSTR(@namespace);
            try
            {
                Check(connectServer(locator, path, 0, 0, 0, 0, 0, null, &services), $"ConnectServer({@namespace})");
            }
            finally
            {
                Marshal.FreeBSTR(path);
            }

            Check(Ole32.CoSetProxyBlanket(services, RPC_C_AUTHN_WINNT, RPC_C_AUTHZ_NONE, null,
                RPC_C_AUTHN_LEVEL_CALL, RPC_C_IMP_LEVEL_IMPERSONATE, null, EOAC_NONE), "CoSetProxyBlanket");

            var connection = new WmiConnection(services, uninitialize);
            services = null; // ownership transferred
            return connection;
        }
        catch
        {
            Release(services);
            if (uninitialize)
            {
                Ole32.CoUninitialize();
            }
            throw;
        }
        finally
        {
            Release(locator);
        }
    }

    /// <summary>Runs a WQL query and materialises every result. Dispose the result set.</summary>
    public WmiResultSet Query(string wql)
    {
        ObjectDisposedException.ThrowIf(_services == null, this);

        void* enumerator = null;
        var language = Marshal.StringToBSTR("WQL");
        var query = Marshal.StringToBSTR(wql);
        try
        {
            // IWbemServices vtable: IUnknown[0..2], ..., ExecQuery[20].
            var execQuery = (delegate* unmanaged<void*, IntPtr, IntPtr, int, void*, void**, int>)VTable(_services)[20];
            Check(execQuery(_services, language, query, WBEM_FLAG_FORWARD_ONLY | WBEM_FLAG_RETURN_IMMEDIATELY, null, &enumerator), wql);
        }
        finally
        {
            Marshal.FreeBSTR(query);
            Marshal.FreeBSTR(language);
        }

        var objects = new List<WmiObject>();
        try
        {
            // IEnumWbemClassObject vtable: IUnknown[0..2], Reset[3], Next[4].
            var next = (delegate* unmanaged<void*, int, uint, void**, uint*, int>)VTable(enumerator)[4];
            while (true)
            {
                void* item = null;
                uint returned = 0;
                var hr = next(enumerator, WBEM_INFINITE, 1, &item, &returned);
                Check(hr, wql); // access-denied surfaces here, not in ExecQuery, with RETURN_IMMEDIATELY
                if (hr != 0 || returned == 0)
                {
                    break; // WBEM_S_FALSE: enumeration complete
                }
                objects.Add(new WmiObject(item));
            }
        }
        catch
        {
            foreach (var o in objects)
            {
                o.Dispose();
            }
            throw;
        }
        finally
        {
            Release(enumerator);
        }

        return new WmiResultSet(objects);
    }

    public void Dispose()
    {
        if (_services != null)
        {
            Release(_services);
            _services = null;
            if (_uninitializeCom)
            {
                Ole32.CoUninitialize();
            }
        }
    }

    internal static void** VTable(void* comObject) => *(void***)comObject;

    internal static void Release(void* comObject)
    {
        if (comObject != null)
        {
            ((delegate* unmanaged<void*, uint>)VTable(comObject)[2])(comObject);
        }
    }

    private static void Check(int hresult, string context)
    {
        if (hresult < 0)
        {
            throw new WmiException(context, hresult);
        }
    }
}

/// <summary>The objects a query returned. Disposing it releases every object.</summary>
internal sealed class WmiResultSet(List<WmiObject> objects) : IDisposable
{
    public int Count => objects.Count;

    public List<WmiObject>.Enumerator GetEnumerator() => objects.GetEnumerator();

    public void Dispose()
    {
        foreach (var o in objects)
        {
            o.Dispose();
        }
        objects.Clear();
    }
}

/// <summary>One WMI instance. Properties are read by name, as text or as an integer.</summary>
internal sealed unsafe class WmiObject : IDisposable
{
    private void* _object;

    internal WmiObject(void* comObject)
    {
        _object = comObject;
    }

    /// <summary>
    /// Reads a property as an integer. Handles every integral VARIANT type WMI emits, including
    /// the CIM quirk that <c>uint64</c> arrives as a decimal string. Null when absent, null-valued,
    /// or not numeric.
    /// </summary>
    public long? GetInteger(string property)
    {
        Variant value;
        if (!TryGet(property, &value))
        {
            return null;
        }

        try
        {
            return value.vt switch
            {
                Variant.VT_I2 => value.iVal,
                Variant.VT_I4 => value.lVal,
                Variant.VT_I8 => value.llVal,
                Variant.VT_UI1 => value.bVal,
                Variant.VT_UI2 => value.uiVal,
                Variant.VT_UI4 => value.ulVal,
                Variant.VT_UI8 when value.ullVal <= long.MaxValue => (long)value.ullVal,
                Variant.VT_BOOL => value.iVal != 0 ? 1 : 0,
                Variant.VT_R4 => (long)value.fltVal,
                Variant.VT_R8 => (long)value.dblVal,
                Variant.VT_BSTR when long.TryParse(Marshal.PtrToStringBSTR(value.bstrVal),
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => null
            };
        }
        finally
        {
            OleAut32.VariantClear(&value);
        }
    }

    /// <summary>Reads a property as text. Numbers are rendered invariantly. Null when absent.</summary>
    public string? GetString(string property)
    {
        Variant value;
        if (!TryGet(property, &value))
        {
            return null;
        }

        try
        {
            return value.vt switch
            {
                Variant.VT_BSTR => Marshal.PtrToStringBSTR(value.bstrVal),
                Variant.VT_BOOL => value.iVal != 0 ? "True" : "False",
                Variant.VT_I2 or Variant.VT_I4 or Variant.VT_I8 or Variant.VT_UI1 or Variant.VT_UI2 or Variant.VT_UI4 or Variant.VT_UI8
                    => GetInteger(property)?.ToString(CultureInfo.InvariantCulture),
                _ => null
            };
        }
        finally
        {
            OleAut32.VariantClear(&value);
        }
    }

    /// <summary>False when the property is missing or null; true leaves a VARIANT the caller must clear.</summary>
    private bool TryGet(string property, Variant* value)
    {
        ObjectDisposedException.ThrowIf(_object == null, this);

        fixed (char* name = property)
        {
            // IWbemClassObject vtable: IUnknown[0..2], GetQualifierSet[3], Get[4].
            var get = (delegate* unmanaged<void*, char*, int, Variant*, int*, int*, int>)WmiConnection.VTable(_object)[4];
            var hr = get(_object, name, 0, value, null, null);
            if (hr < 0)
            {
                return false; // WBEM_E_NOT_FOUND and friends: treat as absent
            }

            if (value->vt is Variant.VT_EMPTY or Variant.VT_NULL)
            {
                OleAut32.VariantClear(value);
                return false;
            }

            return true;
        }
    }

    public void Dispose()
    {
        if (_object != null)
        {
            WmiConnection.Release(_object);
            _object = null;
        }
    }
}

/// <summary>OLE VARIANT: 16-bit type tag, padding, then an 8-byte union. 24 bytes on 64-bit Windows.</summary>
[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct Variant
{
    public const ushort VT_EMPTY = 0;
    public const ushort VT_NULL = 1;
    public const ushort VT_I2 = 2;
    public const ushort VT_I4 = 3;
    public const ushort VT_R4 = 4;
    public const ushort VT_R8 = 5;
    public const ushort VT_BSTR = 8;
    public const ushort VT_BOOL = 11;
    public const ushort VT_UI1 = 17;
    public const ushort VT_UI2 = 18;
    public const ushort VT_UI4 = 19;
    public const ushort VT_I8 = 20;
    public const ushort VT_UI8 = 21;

    [FieldOffset(0)] public ushort vt;
    [FieldOffset(8)] public short iVal;
    [FieldOffset(8)] public int lVal;
    [FieldOffset(8)] public long llVal;
    [FieldOffset(8)] public byte bVal;
    [FieldOffset(8)] public ushort uiVal;
    [FieldOffset(8)] public uint ulVal;
    [FieldOffset(8)] public ulong ullVal;
    [FieldOffset(8)] public float fltVal;
    [FieldOffset(8)] public double dblVal;
    [FieldOffset(8)] public IntPtr bstrVal;
}

internal static unsafe partial class Ole32
{
    [LibraryImport("ole32.dll")]
    internal static partial int CoInitializeEx(void* reserved, uint coInit);

    [LibraryImport("ole32.dll")]
    internal static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    internal static partial int CoCreateInstance(Guid* clsid, void* outer, uint clsContext, Guid* iid, void** instance);

    [LibraryImport("ole32.dll")]
    internal static partial int CoSetProxyBlanket(void* proxy, uint authnSvc, uint authzSvc, void* serverPrincipalName,
        uint authnLevel, uint impersonationLevel, void* authInfo, uint capabilities);
}

internal static unsafe partial class OleAut32
{
    // Declared void: the only failure mode is an invalid VARIANT, which we never construct.
    [LibraryImport("oleaut32.dll")]
    internal static partial void VariantClear(Variant* variant);
}
