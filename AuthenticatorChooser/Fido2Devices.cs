using System.Runtime.InteropServices;

namespace AuthenticatorChooser;

/// <summary>
/// Counts the FIDO2 security keys currently attached to the system by enumerating raw input HID devices whose usage
/// page is the CTAP HID usage page (0xF1D0). This is vendor-neutral (works for any FIDO2 key, not just YubiKey) and
/// used to refuse the PIN cache when more than one key is present: a cached PIN could be fed to the wrong key and,
/// after enough wrong attempts (8 on YubiKeys), permanently lock it.
/// </summary>
internal static class Fido2Devices {

    private const uint   RIDI_DEVICEINFO = 0x2000000b;
    private const uint   RIM_TYPEHID     = 2;
    private const ushort CTAP_USAGE_PAGE = 0xF1D0;

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICELIST {
        public IntPtr hDevice;
        public uint   dwType;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RID_DEVICE_INFO_MOUSE {
        public uint dwId;
        public uint dwNumberOfButtons;
        public uint dwSampleRate;
        public int  fHasHorizontalWheel;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RID_DEVICE_INFO_KEYBOARD {
        public uint dwType;
        public uint dwSubType;
        public uint dwKeyboardMode;
        public uint dwNumberOfFunctionKeys;
        public uint dwNumberOfIndicators;
        public uint dwNumberOfKeysTotal;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RID_DEVICE_INFO_HID {
        public uint   dwVendorId;
        public uint   dwProductId;
        public uint   dwVersionNumber;
        public ushort usUsagePage;
        public ushort usUsage;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct RID_DEVICE_INFO_UNION {
        [FieldOffset(0)] public RID_DEVICE_INFO_MOUSE     mouse;
        [FieldOffset(0)] public RID_DEVICE_INFO_KEYBOARD keyboard;
        [FieldOffset(0)] public RID_DEVICE_INFO_HID       hid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RID_DEVICE_INFO {
        public uint               cbSize;
        public uint               dwType;
        public RID_DEVICE_INFO_UNION u;
    }

    [DllImport("user32.dll")]
    private static extern uint GetRawInputDeviceList(IntPtr pRawInputDeviceList, ref uint puiNumDevices, uint cbSize);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetRawInputDeviceInfoW(IntPtr hDevice, uint uiCommand, IntPtr pData, ref uint pcbSize);

    /// <summary>Number of attached FIDO2 security keys. Returns 0 on any failure, so callers can proceed conservatively.</summary>
    public static int countFido2() {
        uint deviceCount = 0;
        uint itemSize    = (uint) Marshal.SizeOf<RAWINPUTDEVICELIST>();
        if (GetRawInputDeviceList(IntPtr.Zero, ref deviceCount, itemSize) == uint.MaxValue || deviceCount == 0) {
            return 0;
        }

        IntPtr list = Marshal.AllocHGlobal((int) (deviceCount * itemSize));
        try {
            if (GetRawInputDeviceList(list, ref deviceCount, itemSize) == uint.MaxValue) {
                return 0;
            }
            int fido = 0;
            for (uint i = 0; i < deviceCount; i++) {
                var device = Marshal.PtrToStructure<RAWINPUTDEVICELIST>(list + (int) (i * itemSize));
                if (device.dwType != RIM_TYPEHID) {
                    continue;
                }
                if (getUsagePage(device.hDevice) == CTAP_USAGE_PAGE) {
                    fido++;
                }
            }
            return fido;
        } finally {
            Marshal.FreeHGlobal(list);
        }
    }

    private static ushort getUsagePage(IntPtr hDevice) {
        RID_DEVICE_INFO info = new() { cbSize = (uint) Marshal.SizeOf<RID_DEVICE_INFO>() };
        IntPtr          pInfo = Marshal.AllocHGlobal((int) info.cbSize);
        try {
            Marshal.StructureToPtr(info, pInfo, false);
            uint cb = info.cbSize;
            if (GetRawInputDeviceInfoW(hDevice, RIDI_DEVICEINFO, pInfo, ref cb) != 0) {
                return Marshal.PtrToStructure<RID_DEVICE_INFO>(pInfo).u.hid.usUsagePage;
            }
            return 0;
        } catch (Exception) {
            return 0;
        } finally {
            Marshal.FreeHGlobal(pInfo);
        }
    }

}
