using System;
using System.Net.Http;
using System.Net.Http.Headers;

namespace RogueUnicorn.StoreTransfer;

internal static class NexusApiClient
{
    public const string ApplicationName = "RetroRewind ModHub";
    public const string ApplicationVersion = "1.0.3";
    private const string UserAgent = "RetroRewind ModHub/1.0.3";

    public static HttpClient Create(TimeSpan? timeout = null)
    {
        var client = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Application-Name", ApplicationName);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Application-Version", ApplicationVersion);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }
}
