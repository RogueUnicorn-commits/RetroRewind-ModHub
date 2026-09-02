using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;

namespace RogueUnicorn.StoreTransfer;

internal static class NexusSecretStore
{
    private static string SecretPath = Path.Combine(AppContext.BaseDirectory, "Mods", "_downloads", "nexus_api_key.dat");
    private static readonly string LegacySecretPath = Path.Combine(AppContext.BaseDirectory, "_downloads", "nexus_api_key.dat");

    public static void Configure(string modsRoot)
    {
        if (string.IsNullOrWhiteSpace(modsRoot)) return;
        SecretPath = Path.Combine(modsRoot, "_downloads", "nexus_api_key.dat");
    }

    public static string? Load()
    {
        try
        {
            var path = File.Exists(SecretPath) ? SecretPath : LegacySecretPath;
            if (!File.Exists(path)) return null;
            var protectedBytes = File.ReadAllBytes(path);
            if (protectedBytes.Length == 0) return null;
            return Unprotect(protectedBytes);
        }
        catch { return null; }
    }

    public static void Save(string? value)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SecretPath)!);
            if (string.IsNullOrWhiteSpace(value))
            {
                if (File.Exists(SecretPath)) File.Delete(SecretPath);
                return;
            }
            File.WriteAllBytes(SecretPath, Protect(value));
        }
        catch { }
    }

    private static byte[] Protect(string value)
    {
        var plain = System.Text.Encoding.UTF8.GetBytes(value);
        var input = new DATA_BLOB();
        var output = new DATA_BLOB();
        try
        {
            input.pbData = Marshal.AllocHGlobal(plain.Length);
            input.cbData = plain.Length;
            Marshal.Copy(plain, 0, input.pbData, plain.Length);
            if (!CryptProtectData(ref input, null, IntPtr.Zero, null, IntPtr.Zero, 0, ref output))
                throw new SecurityException("Windows could not protect the Nexus API key.");
            var result = new byte[output.cbData];
            Marshal.Copy(output.pbData, result, 0, result.Length);
            return result;
        }
        finally
        {
            if (input.pbData != IntPtr.Zero) Marshal.FreeHGlobal(input.pbData);
            if (output.pbData != IntPtr.Zero) LocalFree(output.pbData);
        }
    }

    private static string Unprotect(byte[] protectedBytes)
    {
        var input = new DATA_BLOB();
        var output = new DATA_BLOB();
        try
        {
            input.pbData = Marshal.AllocHGlobal(protectedBytes.Length);
            input.cbData = protectedBytes.Length;
            Marshal.Copy(protectedBytes, 0, input.pbData, protectedBytes.Length);
            if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref output))
                throw new SecurityException("Windows could not unprotect the Nexus API key.");
            var plain = new byte[output.cbData];
            Marshal.Copy(output.pbData, plain, 0, plain.Length);
            return System.Text.Encoding.UTF8.GetString(plain);
        }
        finally
        {
            if (input.pbData != IntPtr.Zero) Marshal.FreeHGlobal(input.pbData);
            if (output.pbData != IntPtr.Zero) LocalFree(output.pbData);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DATA_BLOB pDataIn, string? szDataDescr, IntPtr pOptionalEntropy,
        string? szPromptStruct, IntPtr pvReserved, int dwFlags, ref DATA_BLOB pDataOut);

    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, IntPtr ppszDataDescr, IntPtr pOptionalEntropy,
        IntPtr pReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);
}
