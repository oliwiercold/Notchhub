using System.Runtime.InteropServices;

namespace NotchHub.Platform.Connectivity.Network.Wlan;

// Native WLAN API (wlanapi.dll) declarations. Struct layouts mirror wlanapi.h / windot11.h exactly; sizes are
// checked by the connectivity harness self-test (x64: WLAN_AVAILABLE_NETWORK 628, WLAN_INTERFACE_INFO 532,
// WLAN_BSS_ENTRY 360, WLAN_CONNECTION_PARAMETERS 40, WLAN_NOTIFICATION_DATA 40).

internal enum Dot11BssType
{
    Infrastructure = 1,
    Independent = 2,
    Any = 3,
}

internal enum WlanInterfaceState
{
    NotReady = 0,
    Connected = 1,
    AdHocNetworkFormed = 2,
    Disconnecting = 3,
    Disconnected = 4,
    Associating = 5,
    Discovering = 6,
    Authenticating = 7,
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct Dot11Ssid
{
    public uint Length;
    public fixed byte Ssid[32];
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct WlanInterfaceInfo
{
    public Guid InterfaceGuid;
    public fixed char Description[256];
    public WlanInterfaceState State;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct WlanAvailableNetwork
{
    public fixed char ProfileName[256];
    public Dot11Ssid Ssid;
    public Dot11BssType BssType;
    public uint NumberOfBssids;
    public int NetworkConnectable;
    public uint NotConnectableReason;
    public uint NumberOfPhyTypes;
    public fixed uint PhyTypes[8];
    public int MorePhyTypes;
    public uint SignalQuality;
    public int SecurityEnabled;
    public uint DefaultAuthAlgorithm;
    public uint DefaultCipherAlgorithm;
    public uint Flags;
    public uint Reserved;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct WlanBssEntry
{
    public Dot11Ssid Ssid;
    public uint PhyId;
    public fixed byte Bssid[6];
    public Dot11BssType BssType;
    public int PhyType;
    public int Rssi;
    public uint LinkQuality;
    public byte InRegDomain;
    public ushort BeaconPeriod;
    public ulong Timestamp;
    public ulong HostTimestamp;
    public ushort CapabilityInformation;
    /// <summary>Channel center frequency in kHz.</summary>
    public uint ChCenterFrequency;
    public uint RateSetLength;
    public fixed ushort RateSet[126];
    public uint IeOffset;
    public uint IeSize;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct WlanConnectionParameters
{
    public int ConnectionMode;
    public char* Profile;
    public Dot11Ssid* Ssid;
    public IntPtr DesiredBssidList;
    public Dot11BssType BssType;
    public uint Flags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WlanNotificationData
{
    public uint NotificationSource;
    public uint NotificationCode;
    public Guid InterfaceGuid;
    public uint DataSize;
    public IntPtr Data;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct WlanConnectionNotificationData
{
    public int ConnectionMode;
    public fixed char ProfileName[256];
    public Dot11Ssid Ssid;
    public Dot11BssType BssType;
    public int SecurityEnabled;
    public uint ReasonCode;
    public uint Flags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WlanPhyRadioState
{
    public uint PhyIndex;
    /// <summary>DOT11_RADIO_STATE: 0 unknown, 1 on, 2 off.</summary>
    public int SoftwareRadioState;
    public int HardwareRadioState;
}

internal static unsafe partial class WlanNative
{
    private const string Lib = "wlanapi.dll";

    public const uint ClientVersion = 2;
    public const uint ErrorSuccess = 0;
    public const uint ErrorAccessDenied = 5;
    public const uint ErrorServiceNotActive = 1062;

    public const uint NotificationSourceNone = 0;
    public const uint NotificationSourceAcm = 0x08;
    public const uint NotificationSourceMsm = 0x10;

    // WLAN_NOTIFICATION_ACM
    public const uint AcmScanComplete = 7;
    public const uint AcmScanFail = 8;
    public const uint AcmConnectionComplete = 10;
    public const uint AcmConnectionAttemptFail = 11;
    public const uint AcmInterfaceArrival = 13;
    public const uint AcmInterfaceRemoval = 14;
    public const uint AcmDisconnected = 21;

    // WLAN_NOTIFICATION_MSM
    public const uint MsmConnected = 4;
    public const uint MsmRadioStateChange = 7;
    public const uint MsmSignalQualityChange = 8;
    public const uint MsmDisconnected = 10;

    public const uint AvailableNetworkConnected = 0x1;
    public const uint AvailableNetworkHasProfile = 0x2;

    public const int ConnectionModeProfile = 0;

    /// <summary>WlanSetProfile flag: per-user profile.</summary>
    public const uint ProfileUser = 0x2;

    // DOT11_AUTH_ALGORITHM
    public const uint AuthOpen = 1;
    public const uint AuthSharedKey = 2;
    public const uint AuthWpa = 3;
    public const uint AuthWpaPsk = 4;
    public const uint AuthRsna = 6;
    public const uint AuthRsnaPsk = 7;
    public const uint AuthWpa3Ent192 = 8;
    public const uint AuthWpa3Sae = 9;
    public const uint AuthOwe = 10;
    public const uint AuthWpa3Ent = 11;

    // DOT11_CIPHER_ALGORITHM
    public const uint CipherNone = 0x00;
    public const uint CipherWep40 = 0x01;
    public const uint CipherTkip = 0x02;
    public const uint CipherCcmp = 0x04;
    public const uint CipherWep104 = 0x05;
    public const uint CipherGcmp = 0x08;
    public const uint CipherGcmp256 = 0x09;
    public const uint CipherWep = 0x101;

    // WLAN_INTF_OPCODE
    public const int IntfOpcodeRadioState = 4;

    public const int Dot11RadioStateOn = 1;

    [LibraryImport(Lib)]
    public static partial uint WlanOpenHandle(uint dwClientVersion, IntPtr pReserved, out uint pdwNegotiatedVersion, out IntPtr phClientHandle);

    [LibraryImport(Lib)]
    public static partial uint WlanCloseHandle(IntPtr hClientHandle, IntPtr pReserved);

    [LibraryImport(Lib)]
    public static partial void WlanFreeMemory(IntPtr pMemory);

    [LibraryImport(Lib)]
    public static partial uint WlanEnumInterfaces(IntPtr hClientHandle, IntPtr pReserved, out IntPtr ppInterfaceList);

    [LibraryImport(Lib)]
    public static partial uint WlanScan(IntPtr hClientHandle, in Guid pInterfaceGuid, IntPtr pDot11Ssid, IntPtr pIeData, IntPtr pReserved);

    [LibraryImport(Lib)]
    public static partial uint WlanGetAvailableNetworkList(IntPtr hClientHandle, in Guid pInterfaceGuid, uint dwFlags, IntPtr pReserved, out IntPtr ppAvailableNetworkList);

    [LibraryImport(Lib)]
    public static partial uint WlanGetNetworkBssList(
        IntPtr hClientHandle, in Guid pInterfaceGuid, IntPtr pDot11Ssid, Dot11BssType dot11BssType,
        [MarshalAs(UnmanagedType.Bool)] bool bSecurityEnabled, IntPtr pReserved, out IntPtr ppWlanBssList);

    [LibraryImport(Lib)]
    public static partial uint WlanConnect(IntPtr hClientHandle, in Guid pInterfaceGuid, WlanConnectionParameters* pConnectionParameters, IntPtr pReserved);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint WlanSetProfile(
        IntPtr hClientHandle, in Guid pInterfaceGuid, uint dwFlags, string strProfileXml, string? strAllUserProfileSecurity,
        [MarshalAs(UnmanagedType.Bool)] bool bOverwrite, IntPtr pReserved, out uint pdwReasonCode);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint WlanDeleteProfile(IntPtr hClientHandle, in Guid pInterfaceGuid, string strProfileName, IntPtr pReserved);

    [LibraryImport(Lib)]
    public static partial uint WlanDisconnect(IntPtr hClientHandle, in Guid pInterfaceGuid, IntPtr pReserved);

    [LibraryImport(Lib)]
    public static partial uint WlanQueryInterface(
        IntPtr hClientHandle, in Guid pInterfaceGuid, int opCode, IntPtr pReserved, out uint pdwDataSize, out IntPtr ppData, out int pWlanOpcodeValueType);

    [LibraryImport(Lib)]
    public static partial uint WlanRegisterNotification(
        IntPtr hClientHandle, uint dwNotifSource, [MarshalAs(UnmanagedType.Bool)] bool bIgnoreDuplicate,
        IntPtr funcCallback, IntPtr pCallbackContext, IntPtr pReserved, out uint pdwPrevNotifSource);
}
