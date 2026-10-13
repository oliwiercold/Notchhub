using System.Runtime.InteropServices;

namespace NotchHub.Platform.Media.Common;

/// <summary>Windows DPAPI (CurrentUser scope) via crypt32 — avoids the ProtectedData NuGet package.</summary>
internal static class Dpapi
{
    private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DataBlob pDataIn, string? szDataDescr, ref DataBlob pOptionalEntropy,
        IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DataBlob pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DataBlob pDataIn, IntPtr ppszDataDescr, ref DataBlob pOptionalEntropy,
        IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DataBlob pDataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    public static byte[] Protect(byte[] data, byte[] entropy) => Run(data, entropy, protect: true);

    public static byte[] Unprotect(byte[] data, byte[] entropy) => Run(data, entropy, protect: false);

    private static byte[] Run(byte[] data, byte[] entropy, bool protect)
    {
        var hData = GCHandle.Alloc(data, GCHandleType.Pinned);
        var hEntropy = GCHandle.Alloc(entropy, GCHandleType.Pinned);
        try
        {
            var input = new DataBlob { cbData = data.Length, pbData = hData.AddrOfPinnedObject() };
            var ent = new DataBlob { cbData = entropy.Length, pbData = hEntropy.AddrOfPinnedObject() };
            var ok = protect
                ? CryptProtectData(ref input, "NotchHub", ref ent, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, out var output)
                : CryptUnprotectData(ref input, IntPtr.Zero, ref ent, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, out output);
            if (!ok) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var result = new byte[output.cbData];
                Marshal.Copy(output.pbData, result, 0, output.cbData);
                return result;
            }
            finally { LocalFree(output.pbData); }
        }
        finally
        {
            hData.Free();
            hEntropy.Free();
        }
    }
}
