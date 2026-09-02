using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Security;

namespace RogueUnicorn.StoreTransfer;

internal static class AccountProfileCache
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string AccountsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Retro Rewind Modhub",
        "Accounts");

    private const string EncryptedSuffix = ".RRModHub.ENCRYPTED";
    private const string LegacyEncryptedSuffix = EncryptedSuffix;

    public static string SteamPngPath => Path.Combine(AccountsDirectory, "Steam");
    public static string NexusJsonPath => Path.Combine(AccountsDirectory, "Nexus.json");
    public static string NexusPngPath => Path.Combine(AccountsDirectory, "Nexus");
    public static string SteamJsonPath => Path.Combine(AccountsDirectory, "Steam.json");

    public static void SaveSteam(SteamAccountCache profile)
        => SaveAccountJson(SteamJsonPath, profile);

    public static SteamAccountCache? LoadSteam()
        => LoadJson<SteamAccountCache>(SteamJsonPath);

    public static void SaveNexus(NexusAccountCache profile)
        => SaveAccountJson(NexusJsonPath, profile);

    public static NexusAccountCache? LoadNexus()
        => LoadJson<NexusAccountCache>(NexusJsonPath);

    private static void SaveAccountJson<T>(string path, T value)
    {
        // Account JSON is encrypted exactly like the profile images, but keeps
        // the original logical filename followed by the required suffix:
        // Steam.json.RRModHub.ENCRYPTED / Nexus.json.RRModHub.ENCRYPTED.
        Directory.CreateDirectory(AccountsDirectory);

        var encryptedPath = path + EncryptedSuffix;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        SaveEncryptedBytes(encryptedPath, bytes);

        // Do not leave an unencrypted JSON copy behind.
        TryDelete(path);

        // Verify the file exists so a failed account-cache write is not silently
        // mistaken for a successful save.
        if (!File.Exists(encryptedPath))
            throw new IOException($"Account cache was not written: {encryptedPath}");
    }

    public static void SaveImage(string path, byte[] bytes, string? sourceUrl = null)
    {
        if (bytes == null || bytes.Length == 0) return;
        Directory.CreateDirectory(AccountsDirectory);

        var extension = GetImageExtension(sourceUrl, bytes);
        var logicalName = Path.GetFileName(path);
        var outputPath = Path.Combine(AccountsDirectory, logicalName + extension + EncryptedSuffix);

        foreach (var existing in Directory.EnumerateFiles(AccountsDirectory, logicalName + ".*" + EncryptedSuffix))
        {
            // The account JSON uses the same logical stem (Nexus/Steam), so it
            // also matches this wildcard. Never delete the JSON cache when
            // rotating avatar formats.
            var existingName = Path.GetFileName(existing);
            var jsonName = logicalName + ".json" + EncryptedSuffix;
            if (string.Equals(existingName, jsonName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.Equals(existing, outputPath, StringComparison.OrdinalIgnoreCase))
                TryDelete(existing);
        }

        SaveEncryptedBytes(outputPath, bytes);
    }

    public static bool TryReadImage(string path, out byte[] bytes)
    {
        try
        {
            var logicalName = Path.GetFileName(path);
            var encrypted = Directory.Exists(AccountsDirectory)
                ? Directory.EnumerateFiles(AccountsDirectory, logicalName + ".*" + EncryptedSuffix).FirstOrDefault()
                : null;
            if (!string.IsNullOrWhiteSpace(encrypted) && TryReadEncryptedBytes(encrypted, out bytes))
                return bytes.Length > 0;

            // Legacy plaintext cache migration. Older builds stored avatars as Steam.png/Nexus.png.
            var legacyCandidates = new[]
            {
                path,
                path + ".png",
                Path.Combine(AccountsDirectory, logicalName + ".png")
            }.Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var legacyPath in legacyCandidates)
            {
                if (!File.Exists(legacyPath)) continue;
                bytes = File.ReadAllBytes(legacyPath);
                if (bytes.Length > 0)
                {
                    SaveImage(path, bytes, legacyPath);
                    TryDelete(legacyPath);
                    return true;
                }
            }
        }
        catch { }

        bytes = Array.Empty<byte>();
        return false;
    }

    private static void SaveJson<T>(string path, T value)
    {
        Directory.CreateDirectory(AccountsDirectory);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        SaveEncryptedBytes(path + EncryptedSuffix, bytes);

        // Remove any older plaintext/incorrectly named encrypted copy after the
        // correctly suffixed encrypted file has been written.
        TryDelete(path);
    }

    private static T? LoadJson<T>(string path)
    {
        try
        {
            // Current format: the encrypted file keeps its original logical name
            // and adds .RRModHub.ENCRYPTED.
            var encryptedPath = path + EncryptedSuffix;
            if (TryReadEncryptedBytes(encryptedPath, out var bytes))
                return JsonSerializer.Deserialize<T>(bytes, JsonOptions);

            // Legacy plaintext cache migration. Read the old .json, then rewrite it
            // using the current .json.RRModHub.ENCRYPTED filename.
            if (File.Exists(path))
            {
                var legacy = File.ReadAllBytes(path);
                var result = JsonSerializer.Deserialize<T>(legacy, JsonOptions);
                SaveEncryptedBytes(encryptedPath, legacy);
                TryDelete(path);
                return result;
            }

            return default;
        }
        catch
        {
            return default;
        }
    }

    private static void SaveEncryptedBytes(string path, byte[] plaintext)
    {
        Directory.CreateDirectory(AccountsDirectory);
        var protectedBytes = Protect(plaintext);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, protectedBytes);
        File.Move(temp, path, true);
    }

    private static bool TryReadEncryptedBytes(string path, out byte[] plaintext)
    {
        try
        {
            if (!File.Exists(path)) { plaintext = Array.Empty<byte>(); return false; }
            var encrypted = File.ReadAllBytes(path);
            plaintext = Unprotect(encrypted);
            return plaintext.Length > 0;
        }
        catch
        {
            plaintext = Array.Empty<byte>();
            return false;
        }
    }

    private static byte[] Protect(byte[] data)
    {
        var description = "Retro Rewind ModHub account cache";
        var input = new DATA_BLOB { cbData = (uint)data.Length, pbData = Marshal.AllocHGlobal(data.Length) };
        var output = new DATA_BLOB();
        try
        {
            Marshal.Copy(data, 0, input.pbData, data.Length);
            if (!CryptProtectData(ref input, description, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out output))
                throw new SecurityException("Windows DPAPI encryption failed.");
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

    private static byte[] Unprotect(byte[] data)
    {
        var input = new DATA_BLOB { cbData = (uint)data.Length, pbData = Marshal.AllocHGlobal(data.Length) };
        var output = new DATA_BLOB();
        try
        {
            Marshal.Copy(data, 0, input.pbData, data.Length);
            if (!CryptUnprotectData(ref input, out output, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out _))
                throw new SecurityException("Windows DPAPI decryption failed.");
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

    private static string GetImageExtension(string? sourceUrl, byte[] bytes)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(sourceUrl))
            {
                var uri = new Uri(sourceUrl, UriKind.Absolute);
                var ext = Path.GetExtension(uri.AbsolutePath);
                if (!string.IsNullOrWhiteSpace(ext) && ext.Length <= 8)
                    return ext.ToLowerInvariant();
            }
        }
        catch { }

        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return ".png";
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return ".jpg";
        if (bytes.Length >= 6 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46) return ".gif";
        if (bytes.Length >= 2 && bytes[0] == 0x42 && bytes[1] == 0x4D) return ".bmp";
        if (bytes.Length >= 4 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46) return ".webp";
        return ".img";
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB
    {
        public uint cbData;
        public IntPtr pbData;
    }

    [DllImport("Crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DATA_BLOB pDataIn, string szDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, uint dwFlags, out DATA_BLOB pDataOut);

    [DllImport("Crypt32.dll", SetLastError = true)]
    private static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, out DATA_BLOB pDataOut, IntPtr ppszDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved, uint dwFlags, out IntPtr pPromptStruct);

    [DllImport("Kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    internal sealed class SteamAccountCache
    {
        public string SteamId64 { get; set; } = "";
        public string AccountName { get; set; } = "";
        public string PersonaName { get; set; } = "";
        public string ProfileUrl { get; set; } = "";
        public string AvatarUrl { get; set; } = "";
        public DateTime LastUpdatedUtc { get; set; }
    }

    internal sealed class NexusAccountCache
    {
        public string UserId { get; set; } = "";
        public string UserName { get; set; } = "";
        public string AccountType { get; set; } = "";
        public string ProfileUrl { get; set; } = "";
        public string AvatarUrl { get; set; } = "";
        public DateTime LastUpdatedUtc { get; set; }
    }
}
