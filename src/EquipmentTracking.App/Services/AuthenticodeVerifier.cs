using System.IO;
using System.Runtime.InteropServices;

namespace EquipmentTracking.App.Services;

internal static class AuthenticodeVerifier
{
    private static readonly Guid GenericVerifyAction =
        new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    public static bool IsTrusted(string filePath)
    {
        var fileInfo = new WinTrustFileInfo
        {
            StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
            FilePath = Path.GetFullPath(filePath),
            FileHandle = IntPtr.Zero,
            KnownSubject = IntPtr.Zero
        };
        var fileInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        Marshal.StructureToPtr(fileInfo, fileInfoPointer, fDeleteOld: false);

        var data = new WinTrustData
        {
            StructSize = (uint)Marshal.SizeOf<WinTrustData>(),
            UiChoice = 2, // WTD_UI_NONE
            RevocationChecks = 0, // WTD_REVOKE_NONE
            UnionChoice = 1, // WTD_CHOICE_FILE
            FileInfoPointer = fileInfoPointer,
            StateAction = 1, // WTD_STATEACTION_VERIFY
            ProviderFlags = 0x00000010 | 0x00001000, // no revocation network; cached URLs only
            UiContext = 0
        };

        try
        {
            var action = GenericVerifyAction;
            return WinVerifyTrust(new IntPtr(-1), ref action, ref data) == 0;
        }
        finally
        {
            if (data.StateData != IntPtr.Zero)
            {
                data.StateAction = 2; // WTD_STATEACTION_CLOSE
                var action = GenericVerifyAction;
                _ = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            }

            Marshal.DestroyStructure<WinTrustFileInfo>(fileInfoPointer);
            Marshal.FreeHGlobal(fileInfoPointer);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint StructSize;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string FilePath;

        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint StructSize;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfoPointer;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false)]
    private static extern int WinVerifyTrust(
        IntPtr windowHandle,
        ref Guid actionId,
        ref WinTrustData trustData);
}
