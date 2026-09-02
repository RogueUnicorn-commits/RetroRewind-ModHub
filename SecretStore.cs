[1mdiff --git a/NexusSecretStore.cs b/NexusSecretStore.cs[m
[1mnew file mode 100644[m
[1mindex 0000000..02ec735[m
[1m--- /dev/null[m
[1m+++ b/NexusSecretStore.cs[m
[36m@@ -0,0 +1,109 @@[m
[32m+[m[32musing System;[m
[32m+[m[32musing System.IO;[m
[32m+[m[32musing System.Runtime.InteropServices;[m
[32m+[m[32musing System.Security;[m
[32m+[m
[32m+[m[32mnamespace RogueUnicorn.StoreTransfer;[m
[32m+[m
[32m+[m[32minternal static class NexusSecretStore[m
[32m+[m[32m{[m
[32m+[m[32m    private static string SecretPath = Path.Combine(AppContext.BaseDirectory, "Mods", "_downloads", "nexus_api_key.dat");[m
[32m+[m[32m    private static readonly string LegacySecretPath = Path.Combine(AppContext.BaseDirectory, "_downloads", "nexus_api_key.dat");[m
[32m+[m
[32m+[m[32m    public static void Configure(string modsRoot)[m
[32m+[m[32m    {[m
[32m+[m[32m        if (string.IsNullOrWhiteSpace(modsRoot)) return;[m
[32m+[m[32m        SecretPath = Path.Combine(modsRoot, "_downloads", "nexus_api_key.dat");[m
[32m+[m[32m    }[m
[32m+[m
[32m+[m[32m    public static string? Load()[m
[32m+[m[32m    {[m
[32m+[m[32m        try[m
[32m+[m[32m        {[m
[32m+[m[32m            var path = File.Exists(SecretPath) ? SecretPath : LegacySecretPath;[m
[32m+[m[32m            if (!File.Exists(path)) return null;[m
[32m+[m[32m            var protectedBytes = File.ReadAllBytes(path);[m
[32m+[m[32m            if (protectedBytes.Length == 0) return null;[m
[32m+[m[32m            return Unprotect(protectedBytes);[m
[32m+[m[32m        }[m
[32m+[m[32m        catch { return null; }[m
[32m+[m[32m    }[m
[32m+[m
[32m+[m[32m    public static void Save(string? value)[m
[32m+[m[32m    {[m
[32m+[m[32m        try[m
[32m+[m[32m        {[m
[32m+[m[32m            Directory.CreateDirectory(Path.GetDirectoryName(SecretPath)!);[m
[32m+[m[32m            if (string.IsNullOrWhiteSpace(value))[m
[32m+[m[32m            {[m
[32m+[m[32m                if (File.Exists(SecretPath)) File.Delete(SecretPath);[m
[32m+[m[32m                return;[m
[32m+[m[32m            }[m
[32m+[m[32m            File.WriteAllBytes(SecretPath, Protect(value));[m
[32m+[m[32m        }[m
[32m+[m[32m        catch { }[m
[32m+[m[32m    }[m
[32m+[m
[32m+[m[32m    private static byte[] Protect(string value)[m
[32m+[m[32m    {[m
[32m+[m[32m        var plain = System.Text.Encoding.UTF8.GetBytes(value);[m
[32m+[m[32m        var input = new DATA_BLOB();[m
[32m+[m[32m        var output = new DATA_BLOB();[m
[32m+[m[32m        try[m
[32m+[m[32m        {[m
[32m+[m[32m            input.pbData = Marshal.AllocHGlobal(plain.Length);[m
[32m+[m[32m            input.cbData = plain.Length;[m
[32m+[m[32m            Marshal.Copy(plain, 0, input.pbData, plain.Length);[m
[32m+[m[32m            if (!CryptProtectData(ref input, null, IntPtr.Zero, null, IntPtr.Zero, 0, ref output))[m
[32m+[m[32m                throw new SecurityException("Windows could not protect the Nexus API key.");[m
[32m+[m[32m            var result = new byte[output.cbData];[m
[32m+[m[32m            Marshal.Copy(output.pbData, result, 0, result.Length);[m
[32m+[m[32m            return result;[m
[32m+[m[32m        }[m
[32m+[m[32m        finally[m
[32m+[m[32m        {[m
[32m+[m[32m            if (input.pbData != IntPtr.Zero) Marshal.FreeHGlobal(input.pbData);[m
[32m+[m[32m            if (output.pbData != IntPtr.Zero) LocalFree(output.pbData);[m
[32m+[m[32m        }[m
[32m+[m[32m    }[m
[32m+[m
[32m+[m[32m    private static string Unprotect(byte[] protectedBytes)[m
[32m+[m[32m    {[m
[32m+[m[32m        var input = new DATA_BLOB();[m
[32m+[m[32m        var output = new DATA_BLOB();[m
[32m+[m[32m        try[m
[32m+[m[32m        {[m
[32m+[m[32m            input.pbData = Marshal.AllocHGlobal(protectedBytes.Length);[m
[32m+[m[32m            input.cbData = protectedBytes.Length;[m
[32m+[m[32m            Marshal.Copy(protectedBytes, 0, input.pbData, protectedBytes.Length);[m
[32m+[m[32m            if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref output))[m
[32m+[m[32m                throw new SecurityException("Windows could not unprotect the Nexus API key.");[m
[32m+[m[32m            var plain = new byte[output.cbData];[m
[32m+[m[32m            Marshal.Copy(output.pbData, plain, 0, plain.Length);[m
[32m+[m[32m            return System.Text.Encoding.UTF8.GetString(plain);[m
[32m+[m[32m        }[m
[32m+[m[32m        finally[m
[32m+[m[32m        {[m
[32m+[m[32m            if (input.pbData != IntPtr.Zero) Marshal.FreeHGlobal(input.pbData);[m
[32m+[m[32m            if (output.pbData != IntPtr.Zero) LocalFree(output.pbData);[m
[32m+[m[32m        }[m
[32m+[m[32m    }[m
[32m+[m
[32m+[m[32m    [StructLayout(LayoutKind.Sequential)][m
[32m+[m[32m    private struct DATA_BLOB[m
[32m+[m[32m    {[m
[32m+[m[32m        public int cbData;[m
[32m+[m[32m        public IntPtr pbData;[m
[32m+[m[32m    }[m
[32m+[m
[32m+[m[32m    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)][m
[32m+[m[32m    private static extern bool CryptProtectData(ref DATA_BLOB pDataIn, string? szDataDescr, IntPtr pOptionalEntropy,[m
[32m+[m[32m        string? szPromptStruct, IntPtr pvReserved, int dwFlags, ref DATA_BLOB pDataOut);[m
[32m+[m
[32m+[m[32m    [DllImport("crypt32.dll", SetLastError = true)][m
[32m+[m[32m    private static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, IntPtr ppszDataDescr, IntPtr pOptionalEntropy,[m
[32m+[m[32m        IntPtr pReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);[m
[32m+[m
[32m+[m[32m    [DllImport("kernel32.dll")][m
[32m+[m[32m    private static extern IntPtr LocalFree(IntPtr hMem);[m
[32m+[m[32m}[m
[1mdiff --git a/NexusSsoClient.cs b/NexusSsoClient.cs[m
[1mnew file mode 100644[m
[1mindex 0000000..14d8f00[m
[1m--- /dev/null[m
[1m+++ b/NexusSsoClient.cs[m
[36m@@ -0,0 +1,107 @@[m
[32m+[m[32musing System;[m
[32m+[m[32musing System.IO;[m
[32m+[m[32musing System.Net.WebSockets;[m
[32m+[m[32musing System.Text;[m
[32m+[m[32musing System.Text.Json;[m
[32m+[m[32musing System.Threading;[m
[32m+[m[32musing System.Threading.Tasks;[m
[32m+[m
[32m+[m[32mnamespace RogueUnicorn.StoreTransfer;[m
[32m+[m
[32m+[m[32minternal static class NexusSsoClient[m
[32m+[m[32m{[m
[32m+[m[32m    // Nexus assigns this application slug when the app is approved for SSO.[m
[32m+[m[32m    // It can be supplied without rebuilding by setting NEXUS_SSO_APP_ID.[m
[32m+[m[32m    private const string DefaultAppId = "";[m
[32m+[m[32m    private const string SsoSocketUrl = "wss://sso.nexusmods.com";[m
[32m+[m[32m    private const string SsoBrowserUrl = "https://www.nexusmods.com/sso?id=";[m
[32m+[m
[32m+[m[32m    public static async Task<string> ConnectAsync(CancellationToken cancellationToken = default)[m
[32m+[m[32m    {[m
[32m+[m[32m        var appId = Environment.GetEnvironmentVariable("NEXUS_SSO_APP_ID")?.Trim();[m
[32m+[m[32m        if (string.IsNullOrWhiteSpace(appId)) appId = DefaultAppId;[m
[32m+[m[32m        if (string.IsNullOrWhiteSpace(appId))[m
[32m+[m[32m            throw new InvalidOperationException([m
[32m+[m[32m                "Nexus SSO is not configured for this build yet. The application must be registered with Nexus Mods and supplied with its assigned SSO app ID.");[m
[32m+[m
[32m+[m[32m        var id = Guid.NewGuid().ToString();[m
[32m+[m[32m        using var socket = new ClientWebSocket();[m
[32m+[m[32m        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);[m
[32m+[m[32m        await socket.ConnectAsync(new Uri(SsoSocketUrl), cancellationToken);[m
[32m+[m
[32m+[m[32m        var payload = JsonSerializer.Serialize(new { id, appid = appId });[m
[32m+[m[32m        var bytes = Encoding.UTF8.GetBytes(payload);[m
[32m+[m[32m        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);[m
[32m+[m
[32m+[m[32m        ProcessBrowser(SsoBrowserUrl + Uri.EscapeDataString(id));[m
[32m+[m
[32m+[m[32m        try[m
[32m+[m[32m        {[m
[32m+[m[32m            var buffer = new byte[4096];[m
[32m+[m[32m            using var message = new MemoryStream();[m
[32m+[m[32m            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)[m
[32m+[m[32m            {[m
[32m+[m[32m                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);[m
[32m+[m[32m                if (result.MessageType == WebSocketMessageType.Close)[m
[32m+[m[32m                    break;[m
[32m+[m[32m                if (result.Count > 0) message.Write(buffer, 0, result.Count);[m
[32m+[m[32m                if (!result.EndOfMessage) continue;[m
[32m+[m
[32m+[m[32m                var text = Encoding.UTF8.GetString(message.ToArray());[m
[32m+[m[32m                message.SetLength(0);[m
[32m+[m[32m                if (!string.IsNullOrWhiteSpace(text))[m
[32m+[m[32m                {[m
[32m+[m[32m                    // Nexus SSO returns the API key as a plain string. Accept JSON[m
[32m+[m[32m                    // too, so the client remains tolerant of response wrappers.[m
[32m+[m[32m                    var apiKey = ExtractApiKey(text);[m
[32m+[m[32m                    if (!string.IsNullOrWhiteSpace(apiKey)) return apiKey;[m
[32m+[m[32m                }[m
[32m+[m[32m            }[m
[32m+[m[32m        }[m
[32m+[m[32m        finally[m
[32m+[m[32m        {[m
[32m+[m[32m            try[m
[32m+[m[32m            {[m
[32m+[m[32m                if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)[m
[32m+[m[32m                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Nexus SSO complete", CancellationToken.None);[m
[32m+[m[32m            }[m
[32m+[m[32m            catch { }[m
[32m+[m[32m        }[m
[32m+[m
[32m+[m[32m        throw new InvalidOperationException("Nexus SSO ended without returning an API key. Please try Connect to Nexus again.");[m
[32m+[m[32m    }[m
[32m+[m
[32m+[m[32m    private static string? ExtractApiKey(string text)[m
[32m+[m[32m    {[m
[32m+[m[32m        text = text.Trim();[m
[32m+[m[32m        if (!text.StartsWith("{")) return text;[m
[32m+[m[32m        try[m
[32m+[m[32m        {[m
[32m+[m[32m            using var doc = JsonDocument.Parse(text);[m
[32m+[m[32m            if (doc.RootElement.ValueKind == JsonValueKind.Object)[m
[32m+[m[32m            {[m
[32m+[m[32m                foreach (var name in new[] { "apikey", "apiKey", "key" })[m
[32m+[m[32m                    if (doc.RootElement.TryGetProperty(name, out var value))[m
[32m+[m[32m                        return value.GetString();[m
[32m+[m[32m            }[m
[32m+[m[32m        }[m
[32m+[m[32m        catch { }[m
[32m+[m[32m        return null;[m
[32m+[m[32m    }[m
[32m+[m
[32m+[m[32m    private static void ProcessBrowser(string url)[m
[32m+[m[32m    {[m
[32m+[m[32m        try[m
[32m+[m[32m        {[m
[32m+[m[32m            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo[m
[32m+[m[32m            {[m
[32m+[m[32m                FileName = url,[m
[32m+[m[32m                UseShellExecute = true[m
[32m+[m[32m            });[m
[32m+[m[32m        }[m
[32m+[m[32m        catch (Exception ex)[m
[32m+[m[32m        {[m
[32m+[m[32m            throw new InvalidOperationException("Could not open the Nexus Mods sign-in page.", ex);[m
[32m+[m[32m        }[m
[32m+[m[32m    }[m
[32m+[m[32m}[m
[1mdiff --git a/SteamSecretStore.cs b/SteamSecretStore.cs[m
[1mnew file mode 100644[m
[1mindex 0000000..728d358[m
[1m--- /dev/null[m
[1m+++ b/SteamSecretStore.cs[m
[36m@@ -0,0 +1,109 @@[m
[32m+[m[32musing System;[m
[32m+[m[32musing System.IO;[m
[32m+[m[32musing System.Runtime.InteropServices;[m
[32m+[m[32musing System.Security;[m
[32m+[m
[32m+[m[32mnamespace RogueUnicorn.StoreTransfer;[m
[32m+[m
[32m+[m[32minternal static class SteamSecretStore[m
[32m+[m[32m{[m
[32m+[m[32m    private static string SecretPath = Path.Combine(AppContext.BaseDirectory, "Mods", "_downloads", "steam_api_key.dat");[m
[32m+[m[32m    private static readonly string LegacySecretPath = Path.Combine(AppContext.BaseDirectory, "_downloads", "steam_api_key.dat");[m
[32m+[m
[32m+[m[32m    public static void Configure(string modsRoot)[m
[32m+[m[32m    {[m
[32m+[m[32m        if (string.IsNullOrWhiteSpace(modsRoot)) return;[m
[32m+[m[32m        SecretPath = Path.Combine(modsRoot, "_downloads", "steam_api_key.dat");[m
[32m+[m[32m    }[m
[32m+[m
[32m+[m[32m    public static string? Load()[m
[32m+[m[32m    {[m
[32m+[m[32m        try[m
[32m+[m[32m        {[m
[32m+[m[32m            var path = File.Exists(SecretPath) ? SecretPath : LegacySecretPath;[m
[32m+[m[32m            if (!File.Exists(path)) return null;[m
[32m+[m[32m            var protectedBytes = File.ReadAllBytes(path);[m
[32m+[m[32m            if (protectedBytes.Length == 0) return null;[m
[32m+[m[32m            return Unprotect(protectedBytes);[m
[32m+[m[32m        }[m
[32m+[m[32m        catch { return null; }[m
[32m+[m[32m    }[m
[32m+[m
[32m+[m[32m    public static void Save(string? value)[m
[32m+[m[32m    {[m
[32m+[m[32m        try[m
[32m+[m[32m        {[m
[32m+[m[32m            Directory.CreateDirectory(Path.GetDirectoryName(SecretPath)!);[m
[32m+[m[32m            if (string.IsNullOrWhiteSpace(value))[m
[32m+[m[32m            {[m
[32m+[m[32m                if (File.Exists(SecretPath)) File.Delete(SecretPath);[m
[32m+[m[32m                return;[m
[32m+[m[32m            }[m
[32m+[m[32m            File.WriteAllBytes(SecretPath, Protect(value));[m
[32m+[m[32m        }[m
[32m+[m[32m        catch { }[m
[32m+[m[32m    }[m
[32m+[m
[32m+[m[32m    private static byte[] Protect(string value)[m
[32m+[m[32m    {[m
[32m+[m[32m        var plain = System.Text.Encoding.UTF8.GetBytes(value);[m
[32m+[m[32m        var input = new DATA_BLOB();[m
[32m+[m[32m        var output = new DATA_BLOB();[m
[32m+[m[32m        try[m
[32m+[m[32m        {[m
[32m+[m[32m            input.pbData = Marshal.AllocHGlobal(plain.Length);[m
[32m+[m[32m            input.cbData = plain.Length;[m
[32m+[m[32m            Marshal.Copy(plain, 0, input.pbData, plain.Length);[m
[32m+[m[32m            if (!CryptProtectData(ref input, null, IntPtr.Zero, null, IntPtr.Zero, 0, ref output))[m
[32m+[m[32m                throw new SecurityException("Windows could not protect the Steam Web API key.");[m
[32m+[m[32m            var result = new byte[output.cbData];[m
[32m+[m[32m            Marshal.Copy(output.pbData, result, 0, result.Length);[m
[32m+[m[32m            return result;[m
[32m+[m[32m        }[m
[32m+[m[32m        finally[m
[32m+[m[32m        {[m
[32m+[m[32m            if (input.pbData != IntPtr.Zero) Marshal.FreeHGlobal(input.pbData);[m
[32m+[m[32m            if (output.pbData != IntPtr.Zero) LocalFree(output.pbData);[m
[32m+[m[32m        }[m
[32m+[m[32m    }[m
[32m+[m
[32m+[m[32m    private static string Unprotect(byte[] protectedBytes)[m
[32m+[m[32m    {[m
[32m+[m[32m        var input = new DATA_BLOB();[m
[32m+[m[32m        var output = new DATA_BLOB();[m
[32m+[m[32m        try[m
[32m+[m[32m        {[m
[32m+[m[32m            input.pbData = Marshal.AllocHGlobal(protectedBytes.Length);[m
[32m+[m[32m            input.cbData = protectedBytes.Length;[m
[32m+[m[32m            Marshal.Copy(protectedBytes, 0, input.pbData, protectedBytes.Length);[m
[32m+[m[32m            if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref output))[m
[32m+[m[32m                throw new SecurityException("Windows could not unprotect the Steam Web API key.");[m
[32m+[m[32m            var plain = new byte[output.cbData];[m
[32m+[m[32m            Marshal.Copy(output.pbData, plain, 0, plain.Length);[m
[32m+[m[32m            return System.Text.Encoding.UTF8.GetString(plain);[m
[32m+[m[32m        }[m
[32m+[m[32m        finally[m
[32m+[m[32m        {[m
[32m+[m[32m            if (input.pbData != IntPtr.Zero) Marshal.FreeHGlobal(input.pbData);[m
[32m+[m[32m            if (output.pbData != IntPtr.Zero) LocalFree(output.pbData);[m
[32m+[m[32m        }[m
[32m+[m[32m    }[m
[32m+[m
[32m+[m[32m    [StructLayout(LayoutKind.Sequential)][m
[32m+[m[32m    private struct DATA_BLOB[m
[32m+[m[32m    {[m
[32m+[m[32m        public int cbData;[m
[32m+[m[32m        public IntPtr pbData;[m
[32m+[m[32m    }[m
[32m+[m
[32m+[m[32m    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)][m
[32m+[m[32m    private static extern bool CryptProtectData(ref DATA_BLOB pDataIn, string? szDataDescr, IntPtr pOptionalEntropy,[m
[32m+[m[32m        string? szPromptStruct, IntPtr pvReserved, int dwFlags, ref DATA_BLOB pDataOut);[m
[32m+[m
[32m+[m[32m    [DllImport("crypt32.dll", SetLastError = true)][m
[32m+[m[32m    private static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, IntPtr ppszDataDescr, IntPtr pOptionalEntropy,[m
[32m+[m[32m        IntPtr pReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);[m
[32m+[m
[32m+[m[32m    [DllImport("kernel32.dll")][m
[32m+[m[32m    private static extern IntPtr LocalFree(IntPtr hMem);[m
[32m+[m[32m}[m
