using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RogueUnicorn.StoreTransfer;

internal static class NexusSsoClient
{
    // Nexus assigns this application slug when the app is approved for SSO.
    // It can be supplied without rebuilding by setting NEXUS_SSO_APP_ID.
    private const string DefaultAppId = "";
    private const string SsoSocketUrl = "wss://sso.nexusmods.com";
    private const string SsoBrowserUrl = "https://www.nexusmods.com/sso?id=";

    public static async Task<string> ConnectAsync(CancellationToken cancellationToken = default)
    {
        var appId = Environment.GetEnvironmentVariable("NEXUS_SSO_APP_ID")?.Trim();
        if (string.IsNullOrWhiteSpace(appId)) appId = DefaultAppId;
        if (string.IsNullOrWhiteSpace(appId))
            throw new InvalidOperationException(
                "Nexus SSO is not configured for this build yet. The application must be registered with Nexus Mods and supplied with its assigned SSO app ID.");

        var id = Guid.NewGuid().ToString();
        using var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
        await socket.ConnectAsync(new Uri(SsoSocketUrl), cancellationToken);

        var payload = JsonSerializer.Serialize(new { id, appid = appId });
        var bytes = Encoding.UTF8.GetBytes(payload);
        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);

        ProcessBrowser(SsoBrowserUrl + Uri.EscapeDataString(id));

        try
        {
            var buffer = new byte[4096];
            using var message = new MemoryStream();
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;
                if (result.Count > 0) message.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage) continue;

                var text = Encoding.UTF8.GetString(message.ToArray());
                message.SetLength(0);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    // Nexus SSO returns the API key as a plain string. Accept JSON
                    // too, so the client remains tolerant of response wrappers.
                    var apiKey = ExtractApiKey(text);
                    if (!string.IsNullOrWhiteSpace(apiKey)) return apiKey;
                }
            }
        }
        finally
        {
            try
            {
                if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Nexus SSO complete", CancellationToken.None);
            }
            catch { }
        }

        throw new InvalidOperationException("Nexus SSO ended without returning an API key. Please try Connect to Nexus again.");
    }

    private static string? ExtractApiKey(string text)
    {
        text = text.Trim();
        if (!text.StartsWith("{")) return text;
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var name in new[] { "apikey", "apiKey", "key" })
                    if (doc.RootElement.TryGetProperty(name, out var value))
                        return value.GetString();
            }
        }
        catch { }
        return null;
    }

    private static void ProcessBrowser(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Could not open the Nexus Mods sign-in page.", ex);
        }
    }
}
