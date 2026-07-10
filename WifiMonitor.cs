using System.Runtime.InteropServices;
using System.Text;

namespace Zeiterfassung;

public enum WifiState
{
    /// <summary>Mit einem WLAN verbunden, SSID bekannt.</summary>
    Connected,
    /// <summary>WLAN-Adapter vorhanden, aber nicht verbunden → "kein WLAN".</summary>
    Disconnected,
    /// <summary>SSID nicht ermittelbar (Standort-Freigabe fehlt) oder kein WLAN-Adapter → NICHT eingreifen.</summary>
    Unavailable
}

public readonly record struct WifiReading(WifiState State, string? Ssid)
{
    public string Key => $"{State}:{Ssid}";
}

/// <summary>
/// Liest die aktuelle WLAN-SSID ueber die Native WiFi API (wlanapi.dll).
/// Bewusst KEIN netsh-Parsing (Feldnamen sind auf DE-Windows lokalisiert).
///
/// Win11-Realitaet: ist die Standort-Freigabe fuer die App/global aus, liefert
/// die API "verbunden" aber leere SSID → wir werten das als <see cref="WifiState.Unavailable"/>
/// und greifen NICHT ein (analog macOS "keine Berechtigung").
///
/// ACHTUNG: P/Invoke-Struct-Offsets sind auf Linux NICHT laufzeit-pruefbar —
/// das ist der erste Punkt, der auf echtem Windows verifiziert werden muss.
/// </summary>
public static class WifiMonitor
{
    public static WifiReading Read()
    {
        IntPtr client = IntPtr.Zero;
        IntPtr ifList = IntPtr.Zero;
        try
        {
            if (WlanOpenHandle(2, IntPtr.Zero, out _, out client) != 0)
                return new WifiReading(WifiState.Unavailable, null);

            if (WlanEnumInterfaces(client, IntPtr.Zero, out ifList) != 0)
                return new WifiReading(WifiState.Unavailable, null);

            uint count = (uint)Marshal.ReadInt32(ifList, 0);
            if (count == 0)
                return new WifiReading(WifiState.Unavailable, null); // kein WLAN-Adapter → nicht eingreifen

            // Header: dwNumberOfItems (4) + dwIndex (4) = 8 Bytes, dann Array.
            const int headerSize = 8;
            int itemSize = Marshal.SizeOf<WLAN_INTERFACE_INFO>();

            for (int i = 0; i < count; i++)
            {
                var infoPtr = ifList + headerSize + i * itemSize;
                var info = Marshal.PtrToStructure<WLAN_INTERFACE_INFO>(infoPtr);

                if (info.isState != WLAN_INTERFACE_STATE.connected)
                    continue; // dieses Interface nicht verbunden

                var reading = ReadConnection(client, info.InterfaceGuid);
                if (reading is { } r) return r;
            }

            // Adapter da, aber keins verbunden.
            return new WifiReading(WifiState.Disconnected, null);
        }
        catch
        {
            return new WifiReading(WifiState.Unavailable, null);
        }
        finally
        {
            if (ifList != IntPtr.Zero) WlanFreeMemory(ifList);
            if (client != IntPtr.Zero) WlanCloseHandle(client, IntPtr.Zero);
        }
    }

    private static WifiReading? ReadConnection(IntPtr client, Guid guid)
    {
        IntPtr data = IntPtr.Zero;
        try
        {
            int rc = WlanQueryInterface(client, ref guid,
                WLAN_INTF_OPCODE.current_connection, IntPtr.Zero,
                out _, out data, IntPtr.Zero);
            if (rc != 0 || data == IntPtr.Zero) return null;

            // WLAN_CONNECTION_ATTRIBUTES-Offsets:
            //   isState(4) + wlanConnectionMode(4) + strProfileName[256]*WCHAR(512) = 520
            //   dann DOT11_SSID { ULONG uSSIDLength; UCHAR ucSSID[32]; }
            const int ssidLenOffset = 8 + 512;      // 520
            const int ssidOffset = ssidLenOffset + 4; // 524

            uint ssidLen = (uint)Marshal.ReadInt32(data, ssidLenOffset);
            if (ssidLen == 0 || ssidLen > 32)
                return new WifiReading(WifiState.Unavailable, null); // verbunden, aber SSID verdeckt → nicht eingreifen

            var bytes = new byte[ssidLen];
            Marshal.Copy(data + ssidOffset, bytes, 0, (int)ssidLen);
            var ssid = Encoding.UTF8.GetString(bytes);
            return new WifiReading(WifiState.Connected, ssid);
        }
        finally
        {
            if (data != IntPtr.Zero) WlanFreeMemory(data);
        }
    }

    // ---- P/Invoke: wlanapi.dll ----

    [DllImport("wlanapi.dll")]
    private static extern int WlanOpenHandle(uint dwClientVersion, IntPtr pReserved,
        out uint pdwNegotiatedVersion, out IntPtr phClientHandle);

    [DllImport("wlanapi.dll")]
    private static extern int WlanCloseHandle(IntPtr hClientHandle, IntPtr pReserved);

    [DllImport("wlanapi.dll")]
    private static extern int WlanEnumInterfaces(IntPtr hClientHandle, IntPtr pReserved,
        out IntPtr ppInterfaceList);

    [DllImport("wlanapi.dll")]
    private static extern int WlanQueryInterface(IntPtr hClientHandle, ref Guid pInterfaceGuid,
        WLAN_INTF_OPCODE OpCode, IntPtr pReserved, out uint pdwDataSize, out IntPtr ppData,
        IntPtr pWlanOpcodeValueType);

    [DllImport("wlanapi.dll")]
    private static extern void WlanFreeMemory(IntPtr pMemory);

    private enum WLAN_INTF_OPCODE : uint
    {
        current_connection = 7
    }

    private enum WLAN_INTERFACE_STATE : uint
    {
        not_ready = 0, connected = 1, ad_hoc = 2, disconnecting = 3,
        disconnected = 4, associating = 5, discovering = 6, authenticating = 7
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WLAN_INTERFACE_INFO
    {
        public Guid InterfaceGuid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string strInterfaceDescription;
        public WLAN_INTERFACE_STATE isState;
    }
}
