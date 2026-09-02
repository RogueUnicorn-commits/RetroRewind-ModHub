using System.Text;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Globalization;
using System;
using Microsoft.Win32;
using System.Diagnostics;
using System.Reflection;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Media.Animation;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Documents;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Web.WebView2.Core;
using SharpVectors.Converters;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.IO.Pipes;
using LibVLCSharp.Shared;
using VlcMediaPlayer = LibVLCSharp.Shared.MediaPlayer;
using VlcVideoView = LibVLCSharp.WPF.VideoView;
using Forms = System.Windows.Forms;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using ListBox = System.Windows.Controls.ListBox;
using Panel = System.Windows.Controls.Panel;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using DragEventArgs = System.Windows.DragEventArgs;

using System.Net.Http;
using SharpCompress.Archives;
using SharpCompress.Common;
using System.Xml.Linq;
namespace RogueUnicorn.StoreTransfer;

public partial class MainWindow : Window
{
    private void AddNexusActionButtons(Panel nav, NexusModMetadata meta, ScrollViewer content)
    {
        var endorse = new Button
        {
            Content = L(meta.Endorsed == true ? "Unendorse" : "Endorse"),
            Style = (Style)Resources["BrowseButtonStyle"],
            Margin = new Thickness(0, 0, 7, 7),
            Padding = new Thickness(12, 7, 12, 7),
            Tag = "endorse"
        };
        var track = new Button
        {
            Content = L(meta.Tracked == true ? "Untrack" : "Track"),
            Style = (Style)Resources["BrowseButtonStyle"],
            Margin = new Thickness(0, 0, 7, 7),
            Padding = new Thickness(12, 7, 12, 7),
            Tag = "track"
        };
        var vote = new Button
        {
            Content = L("Vote"),
            Style = (Style)Resources["BrowseButtonStyle"],
            Margin = new Thickness(0, 0, 7, 7),
            Padding = new Thickness(12, 7, 12, 7),
            Tag = "vote"
        };

        var remaining = GetNexusEndorsementRemaining(meta);
        if (meta.Endorsed != true && remaining > TimeSpan.Zero)
        {
            endorse.IsEnabled = false;
            endorse.ToolTip = L("Endorse available in {0}", FormatNexusCooldown(remaining));
        }
        else if (meta.Endorsed != true && !meta.DownloadedAtUtc.HasValue)
        {
            // Existing installations may predate the local download timestamp. Do not
            // guess that the 15-minute Nexus cooldown has elapsed.
            endorse.IsEnabled = false;
            endorse.ToolTip = L("Endorse becomes available after a Nexus download has been recorded.");
        }

        endorse.Click += async (_, _) =>
        {
            endorse.IsEnabled = false;
            try
            {
                var endorsed = meta.Endorsed == true;
                await SetNexusEndorsementAsync(meta, !endorsed);
                meta = UpdateNexusMetadataState(meta, endorsed: !endorsed);
                endorse.Content = L(meta.Endorsed == true ? "Unendorse" : "Endorse");
                if (meta.Endorsed != true)
                {
                    var wait = GetNexusEndorsementRemaining(meta);
                    endorse.IsEnabled = wait <= TimeSpan.Zero && meta.DownloadedAtUtc.HasValue;
                    endorse.ToolTip = endorse.IsEnabled ? null : L("Endorse available in {0}", FormatNexusCooldown(wait));
                }
                else
                {
                    endorse.IsEnabled = true;
                    endorse.ToolTip = null;
                }
            }
            catch (Exception ex)
            {
                endorse.IsEnabled = true;
                MessageBox.Show(this, ex.Message, L("Nexus"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };

        track.Click += async (_, _) =>
        {
            track.IsEnabled = false;
            try
            {
                var tracked = meta.Tracked == true;
                await SetNexusTrackingAsync(meta, !tracked);
                meta = UpdateNexusMetadataState(meta, tracked: !tracked);
                track.Content = L(meta.Tracked == true ? "Untrack" : "Track");
                track.IsEnabled = true;
            }
            catch (Exception ex)
            {
                track.IsEnabled = true;
                MessageBox.Show(this, ex.Message, L("Nexus"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };

        vote.Click += async (_, _) =>
        {
            vote.IsEnabled = false;
            try
            {
                await OpenNexusVoteViewAsync(content, meta);
            }
            finally
            {
                vote.IsEnabled = true;
            }
        };

        nav.Children.Add(endorse);
        nav.Children.Add(track);
        nav.Children.Add(vote);

        _ = InitializeNexusActionStateAsync(meta, endorse, track);
    }

    private void AddNexusOpenBrowserButton(Panel panel, NexusModMetadata meta)
    {
        var browserName = GetDefaultBrowserDisplayName();
        var button = new Button
        {
            Content = L("Open in {0}", browserName),
            Style = (Style)Resources["BrowseButtonStyle"],
            Margin = new Thickness(0, 0, 7, 7),
            Padding = new Thickness(12, 7, 12, 7),
            ToolTip = L("Open this Nexus mod page in your default browser.")
        };
        button.Click += (_, _) =>
        {
            try
            {
                var url = $"https://www.nexusmods.com/{Uri.EscapeDataString(meta.Game)}/mods/{meta.ModId}";
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, L("Unable to open the Nexus page.\n\n{0}", ex.Message), L("Nexus"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        panel.Children.Add(button);
    }

    private string GetDefaultBrowserDisplayName()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice");
            var progId = key?.GetValue("ProgId")?.ToString() ?? string.Empty;
            if (progId.Contains("MSEdge", StringComparison.OrdinalIgnoreCase)) return "Microsoft Edge";
            if (progId.Contains("Chrome", StringComparison.OrdinalIgnoreCase)) return "Google Chrome";
            if (progId.Contains("Firefox", StringComparison.OrdinalIgnoreCase)) return "Firefox";
            if (progId.Contains("Brave", StringComparison.OrdinalIgnoreCase)) return "Brave";
            if (progId.Contains("Opera", StringComparison.OrdinalIgnoreCase)) return "Opera";
            if (progId.Contains("Vivaldi", StringComparison.OrdinalIgnoreCase)) return "Vivaldi";
            if (progId.Contains("Chromium", StringComparison.OrdinalIgnoreCase)) return "Chromium";
            if (!string.IsNullOrWhiteSpace(progId)) return progId;
        }
        catch { }
        return L("Default Browser");
    }

    private NexusModMetadata UpdateNexusMetadataState(NexusModMetadata meta, bool? endorsed = null, bool? tracked = null) => meta with
    {
        Endorsed = endorsed ?? meta.Endorsed,
        Tracked = tracked ?? meta.Tracked
    };

    private void SaveNexusMetadataState(NexusModMetadata meta)
    {
        var data = LoadNexusMetadata();
        foreach (var key in data.Keys.ToList())
        {
            var current = data[key];
            if (current.ModId == meta.ModId && current.Game.Equals(meta.Game, StringComparison.OrdinalIgnoreCase) &&
                !key.StartsWith("_download:", StringComparison.OrdinalIgnoreCase))
            {
                data[key] = meta;
                SaveNexusMetadata(data);
                return;
            }
        }
    }

    private async Task InitializeNexusActionStateAsync(NexusModMetadata meta, Button endorse, Button track)
    {
        try
        {
            var apiKey = string.IsNullOrWhiteSpace(_nexusApiKey) ? NexusSecretStore.Load() : _nexusApiKey;
            if (string.IsNullOrWhiteSpace(apiKey)) return;
            using var client = CreateNexusHttpClient(apiKey);
            var tracked = await IsNexusModTrackedAsync(client, meta.Game, meta.ModId);
            if (tracked.HasValue)
            {
                track.Content = L(tracked.Value ? "Untrack" : "Track");
                SaveNexusMetadataState(UpdateNexusMetadataState(meta, tracked: tracked.Value));
            }
            var remaining = GetNexusEndorsementRemaining(meta);
            if (meta.Endorsed != true)
            {
                endorse.IsEnabled = meta.DownloadedAtUtc.HasValue && remaining <= TimeSpan.Zero;
                endorse.ToolTip = endorse.IsEnabled ? null : L("Endorse available in {0}", FormatNexusCooldown(remaining));
            }
        }
        catch (Exception ex)
        {
            CrashLogger.Write("NexusActionState", ex);
        }
    }

    private static HttpClient CreateNexusHttpClient(string apiKey)
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RetroRewindModHub/1.0.0");
        client.DefaultRequestHeaders.TryAddWithoutValidation("apikey", apiKey);
        return client;
    }

    private static async Task<bool?> IsNexusModTrackedAsync(HttpClient client, string game, int modId)
    {
        using var response = await client.GetAsync("https://api.nexusmods.com/v1/user/tracked_mods.json");
        if (!response.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var itemGame = JsonString(item, "domain_name", "game_domain", "game");
            var itemId = JsonInt(item, "mod_id", "id");
            if (itemId == modId && itemGame.Equals(game, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private async Task SetNexusEndorsementAsync(NexusModMetadata meta, bool endorse)
    {
        var apiKey = string.IsNullOrWhiteSpace(_nexusApiKey) ? NexusSecretStore.Load() : _nexusApiKey;
        if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException(L("Connect to Nexus in Settings first."));
        if (!endorse && meta.Endorsed != true)
            throw new InvalidOperationException(L("This mod is not currently marked as endorsed in Retro Rewind."));
        if (endorse && GetNexusEndorsementRemaining(meta) > TimeSpan.Zero)
            throw new InvalidOperationException(L("Nexus requires 15 minutes to pass after download before a mod can be endorsed."));
        using var client = CreateNexusHttpClient(apiKey);
        var endpoint = endorse ? "endorse" : "abstain";
        var url = $"https://api.nexusmods.com/v1/games/{Uri.EscapeDataString(meta.Game)}/mods/{meta.ModId}/{endpoint}.json";
        using var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string,string>("Version", meta.LatestVersion ?? "") });
        using var response = await client.PostAsync(url, content);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(L("Nexus could not change the endorsement (HTTP {0}). {1}", (int)response.StatusCode, body));
        }
    }

    private async Task SetNexusTrackingAsync(NexusModMetadata meta, bool track)
    {
        var apiKey = string.IsNullOrWhiteSpace(_nexusApiKey) ? NexusSecretStore.Load() : _nexusApiKey;
        if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException(L("Connect to Nexus in Settings first."));
        using var client = CreateNexusHttpClient(apiKey);
        var url = "https://api.nexusmods.com/v1/user/tracked_mods.json";
        if (track)
        {
            using var body = new StringContent(JsonSerializer.Serialize(new { domain_name = meta.Game, mod_id = meta.ModId }), Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(url, body);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(L("Nexus could not track this mod (HTTP {0}).", (int)response.StatusCode));
        }
        else
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { domain_name = meta.Game, mod_id = meta.ModId }), Encoding.UTF8, "application/json")
            };
            using var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(L("Nexus could not untrack this mod (HTTP {0}).", (int)response.StatusCode));
        }
    }

    private async Task OpenNexusVoteViewAsync(ScrollViewer host, NexusModMetadata meta)
    {
        var web = new WebView2 { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        host.Content = web;
        await web.EnsureCoreWebView2Async();
        web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
        web.CoreWebView2.Settings.IsStatusBarEnabled = false;
        web.CoreWebView2.NavigationCompleted += async (_, _) =>
        {
            try
            {
                // Nexus voting is a site action rather than a documented v1 REST mutation.
                // Load the authenticated page in WebView2 so the user's Nexus session can
                // perform the vote with the site's own controls.
                await web.CoreWebView2.ExecuteScriptAsync(@"(() => {
                    const nodes = [...document.querySelectorAll('button,a')];
                    const n = nodes.find(x => (x.innerText || '').trim().toLowerCase() === 'vote');
                    if (n) { n.scrollIntoView({block:'center'}); n.style.outline='3px solid #ff9800'; }
                })();");
            }
            catch { }
        };
        web.CoreWebView2.Navigate($"https://www.nexusmods.com/{meta.Game}/mods/{meta.ModId}");
    }

    private static TimeSpan GetNexusEndorsementRemaining(NexusModMetadata meta)
    {
        if (meta.DownloadedAtUtc is not DateTime downloaded) return TimeSpan.MaxValue;
        return downloaded.ToUniversalTime().AddMinutes(15) - DateTime.UtcNow;
    }

    private static string FormatNexusCooldown(TimeSpan remaining)
    {
        if (remaining == TimeSpan.MaxValue) return "15 minutes after download";
        if (remaining <= TimeSpan.Zero) return "now";
        return remaining.TotalHours >= 1 ? $"{(int)remaining.TotalHours}:{remaining.Minutes:00}:{remaining.Seconds:00}" : $"{remaining.Minutes}:{remaining.Seconds:00}";
    }

    private void AddNexusNativeButton(
        WrapPanel nav,
        OverlayDialogHost host,
        NexusModMetadata meta,
        ScrollViewer content,
        string label,
        string tab,
        bool visible)
    {
        if (!visible) return;
        var button = new Button
        {
            Content = label,
            Style = (Style)Resources["BrowseButtonStyle"],
            Margin = new Thickness(0, 0, 7, 7),
            Padding = new Thickness(12, 7, 12, 7),
            Tag = tab
        };
        button.Click += async (_, _) => await LoadNexusNativeTabAsync(content, meta, tab);
        nav.Children.Add(button);

        // Description is the initial view.
        if (tab == "description")
            _ = LoadNexusNativeTabAsync(content, meta, tab);
    }

    private void SetNexusStatus(ScrollViewer host, bool online, string? detail = null)
    {
        if (host.DataContext is not TextBlock status) return;
        status.Text = online
            ? L("Nexus: Online")
            : string.IsNullOrWhiteSpace(detail) ? L("Nexus: Offline") : L("Nexus: Offline · {0}", detail);
        status.Foreground = (Brush)Resources[online ? "AccentBrush" : "SecondaryBrush"];
    }

    private async Task LoadNexusNativeTabAsync(ScrollViewer host, NexusModMetadata meta, string tab)
    {
        try
        {
            host.Content = new TextBlock
            {
                Text = L("Loading…"),
                Foreground = (Brush)Resources["SecondaryBrush"],
                FontSize = 14,
                Margin = new Thickness(8)
            };

            var apiKey = string.IsNullOrWhiteSpace(_nexusApiKey) ? NexusSecretStore.Load() : _nexusApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                host.Content = new TextBlock
                {
                    Text = L("Connect to Nexus in Settings to view this content."),
                    Foreground = (Brush)Resources["SecondaryBrush"],
                    Margin = new Thickness(8)
                };
                return;
            }

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("RetroRewindModHub/1.0.0");
            client.DefaultRequestHeaders.TryAddWithoutValidation("apikey", apiKey);

            // Always fetch the primary mod endpoint for Description so we retain
            // the original Nexus BBCode/HTML markup. Cached metadata from older
            // builds may already have had formatting stripped.
            if (tab.Equals("description", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var descriptionUrl = $"https://api.nexusmods.com/v1/games/{Uri.EscapeDataString(meta.Game)}/mods/{meta.ModId}";
                    using var descriptionResponse = await client.GetAsync(descriptionUrl);
                    if (descriptionResponse.IsSuccessStatusCode)
                    {
                        var descriptionJson = await descriptionResponse.Content.ReadAsStringAsync();
                        using var descriptionDoc = JsonDocument.Parse(descriptionJson);
                        if (descriptionDoc.RootElement.TryGetProperty("description", out var freshDescription))
                        {
                            var freshRaw = freshDescription.GetString() ?? "";
                            var freshName = descriptionDoc.RootElement.TryGetProperty("name", out var freshNameElement)
                                ? freshNameElement.GetString() ?? meta.Name : meta.Name;
                            var freshVersion = descriptionDoc.RootElement.TryGetProperty("version", out var freshVersionElement)
                                ? freshVersionElement.GetString() ?? meta.LatestVersion : meta.LatestVersion;
                            SaveNexusDescriptionCache(meta.Game, meta.ModId, freshName, freshVersion, freshRaw);
                        }
                        host.Content = await BuildNexusNativeContentAsync(descriptionDoc.RootElement, tab, meta);
                        SetNexusStatus(host, true);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    CrashLogger.Write("NexusDescriptionFetch", ex);
                }

                // If the API cannot be reached, use the last successful JSON cache.
                if (TryLoadNexusDescriptionCache(meta.Game, meta.ModId, out var cachedDescription))
                {
                    host.Content = await BuildNexusNativeContentAsync(
                        JsonSerializer.SerializeToElement(new { description = cachedDescription.Description }),
                        tab,
                        meta,
                        offline: true);
                    SetNexusStatus(host, false, L("Cached data"));
                }
                else
                {
                    host.Content = await BuildNexusNativeContentAsync(
                        JsonSerializer.SerializeToElement(new { description = meta.Description }),
                        tab,
                        meta,
                        offline: true);
                    SetNexusStatus(host, false);
                }
                return;
            }


            var url = $"https://api.nexusmods.com/v1/games/{Uri.EscapeDataString(meta.Game)}/mods/{meta.ModId}/files.json";
            using var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                host.Content = new TextBlock
                {
                    Text = L("Nexus could not provide the file list right now (HTTP {0}).", (int)response.StatusCode),
                    Foreground = (Brush)Resources["SecondaryBrush"],
                    Margin = new Thickness(8),
                    TextWrapping = TextWrapping.Wrap
                };
                return;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            host.Content = await BuildNexusNativeContentAsync(document.RootElement, tab, meta);
            SetNexusStatus(host, true);
        }
        catch (Exception ex)
        {
            host.Content = new TextBlock
            {
                Text = L("Unable to load Nexus content.\n\n{0}", ex.Message),
                Foreground = (Brush)Resources["SecondaryBrush"],
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(8)
            };
        }
    }

    private async Task<FrameworkElement> BuildNexusNativeContentAsync(JsonElement root, string tab, NexusModMetadata meta, bool offline = false)
    {
        if (tab.Equals("description", StringComparison.OrdinalIgnoreCase))
        {
            var raw = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("description", out var d)
                ? d.GetString() ?? "" : "";
            return BuildNexusDescriptionContent(raw, offline);
        }

        var items = root.ValueKind == JsonValueKind.Array
            ? root.EnumerateArray().ToList()
            : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array
                ? files.EnumerateArray().ToList()
                : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array
                    ? results.EnumerateArray().ToList()
                    : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
                        ? data.EnumerateArray().ToList()
                        : new List<JsonElement>();

        // Nexus file categories are presented in a stable, user-friendly order.
        // Empty categories are omitted. Within each category, newest versions appear first.
        if (tab.Equals("files", StringComparison.OrdinalIgnoreCase))
        {
            var categoryOrder = new[] { "main", "update", "optional", "miscellaneous", "old", "archived" };
            var categoryLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["main"] = L("Main"),
                ["update"] = L("Update"),
                ["optional"] = L("Optional"),
                ["miscellaneous"] = L("Miscellaneous"),
                ["old"] = L("Old"),
                ["archived"] = L("Archived")
            };

            string NormalizeCategory(JsonElement item)
            {
                var category = JsonString(item, "category_name", "category", "categoryName").Trim();
                if (category.Equals("main", StringComparison.OrdinalIgnoreCase) || category.Equals("main file", StringComparison.OrdinalIgnoreCase)) return "main";
                if (category.Equals("update", StringComparison.OrdinalIgnoreCase) || category.Equals("updates", StringComparison.OrdinalIgnoreCase)) return "update";
                if (category.Equals("optional", StringComparison.OrdinalIgnoreCase) || category.Equals("optional file", StringComparison.OrdinalIgnoreCase)) return "optional";
                if (category.Equals("miscellaneous", StringComparison.OrdinalIgnoreCase) || category.Equals("misc", StringComparison.OrdinalIgnoreCase)) return "miscellaneous";
                if (category.Equals("old", StringComparison.OrdinalIgnoreCase) || category.Equals("old file", StringComparison.OrdinalIgnoreCase)) return "old";
                if (category.Equals("archived", StringComparison.OrdinalIgnoreCase) || category.Equals("archive", StringComparison.OrdinalIgnoreCase)) return "archived";
                return "miscellaneous";
            }

            int CompareVersionDescending(string left, string right)
            {
                var l = Regex.Matches(left ?? string.Empty, @"\d+(?:\.\d+)*")
                    .Select(m => m.Value.Split('.').Select(x => int.TryParse(x, out var n) ? n : 0).ToArray())
                    .FirstOrDefault();
                var r = Regex.Matches(right ?? string.Empty, @"\d+(?:\.\d+)*")
                    .Select(m => m.Value.Split('.').Select(x => int.TryParse(x, out var n) ? n : 0).ToArray())
                    .FirstOrDefault();
                if (l == null && r == null) return string.Compare(right, left, StringComparison.OrdinalIgnoreCase);
                if (l == null) return 1;
                if (r == null) return -1;
                var count = Math.Max(l.Length, r.Length);
                for (var i = 0; i < count; i++)
                {
                    var lv = i < l.Length ? l[i] : 0;
                    var rv = i < r.Length ? r[i] : 0;
                    if (lv != rv) return rv.CompareTo(lv);
                }
                return string.Compare(right, left, StringComparison.OrdinalIgnoreCase);
            }

            var currentNexusFileCount = items.Count(item =>
            {
                var category = NormalizeCategory(item);
                return !category.Equals("old", StringComparison.OrdinalIgnoreCase) &&
                       !category.Equals("archived", StringComparison.OrdinalIgnoreCase);
            });

            var grouped = items
                .Select(item => new { Item = item, Category = NormalizeCategory(item), Version = JsonString(item, "version", "mod_version") })
                .GroupBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => Array.IndexOf(categoryOrder, g.Key))
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            var fileRoot = new StackPanel();
            foreach (var group in grouped)
            {
                var groupContent = new StackPanel { Margin = new Thickness(0, 2, 0, 6) };
                foreach (var entry in group.OrderBy(x => x.Version, Comparer<string>.Create(CompareVersionDescending)))
                {
                    var item = entry.Item;
                    var card = new Border
                    {
                        Background = (Brush)Resources["CardBrush"],
                        BorderBrush = (Brush)Resources["BorderBrush"],
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(12),
                        Margin = new Thickness(0, 0, 0, 8)
                    };
                    var body = new StackPanel();
                    var title = JsonString(item, "name", "file_name");
                    var fileId = JsonInt(item, "file_id", "id");
                    var fileName = JsonString(item, "name", "file_name");
                    var fileVersion = JsonString(item, "version", "mod_version");
                    var sizeText = FormatNexusFileSize(item);
                    var downloadedPath = FindNexusDownloadedFile(meta.Game, meta.ModId, fileId);

                    body.Children.Add(new TextBlock
                    {
                        Text = string.IsNullOrWhiteSpace(title) ? L("File") : title,
                        FontSize = 16,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = (Brush)Resources["ForegroundBrush"],
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });

                    var details = string.Join("  •  ", new[] { fileVersion, sizeText }.Where(x => !string.IsNullOrWhiteSpace(x)));
                    if (!string.IsNullOrWhiteSpace(details))
                        body.Children.Add(new TextBlock { Text = details, Margin = new Thickness(0, 4, 0, 8), Foreground = (Brush)Resources["SecondaryBrush"] });

                    var downloadActive = fileId > 0 && IsNexusDownloadActive(meta.Game, meta.ModId, fileId);
                    var download = new Button
                    {
                        Content = downloadedPath != null ? L("Downloaded") : downloadActive ? L("Downloading…") : L("Download"),
                        Style = (Style)Resources["BrowseButtonStyle"],
                        HorizontalAlignment = HorizontalAlignment.Left,
                        IsEnabled = fileId > 0 && downloadedPath == null && !downloadActive,
                        Tag = new NexusFileDownloadRequest(meta.Game, meta.ModId, fileId, fileName, fileVersion, currentNexusFileCount)
                    };
                    download.Click += async (_, _) =>
                    {
                        if (download.Tag is not NexusFileDownloadRequest request) return;
                        try
                        {
                            download.IsEnabled = false;
                            download.Content = L("Downloading…");
                            await DownloadNexusFileAsync(request);
                            download.Content = L("Downloaded");
                        }
                        catch (Exception ex)
                        {
                            download.Content = L("Download");
                            download.IsEnabled = true;
                            MessageBox.Show(this, ex.Message, L("Mod Download"), MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    };
                    body.Children.Add(download);
                    card.Child = body;
                    groupContent.Children.Add(card);
                }

                var expander = new Expander
                {
                    IsExpanded = false,
                    Margin = new Thickness(0, 6, 0, 4),
                    Foreground = (Brush)Resources["ForegroundBrush"],
                    Background = Brushes.Transparent,
                    BorderBrush = Brushes.Transparent,
                    Header = new TextBlock
                    {
                        Text = categoryLabels.TryGetValue(group.Key, out var label) ? label.ToUpperInvariant() : group.Key.ToUpperInvariant(),
                        FontSize = 13,
                        FontWeight = FontWeights.Bold,
                        Foreground = (Brush)Resources["ForegroundBrush"]
                    },
                    Content = groupContent
                };
                fileRoot.Children.Add(expander);
            }
            return fileRoot.Children.Count == 0 ? EmptyNexusContent(L("No files are available.")) : fileRoot;
        }

        var stack = new StackPanel();
        foreach (var item in items)
        {
            var card = new Border
            {
                Background = (Brush)Resources["CardBrush"],
                BorderBrush = (Brush)Resources["BorderBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 8)
            };
            var body = new StackPanel();
            var title = JsonString(item, "name", "title", "subject", "file_name");
            var author = JsonString(item, "author", "user", "username", "uploaded_by");
            var version = JsonString(item, "version", "version", "category_name");
            var date = JsonString(item, "date", "created_at", "updated_at");
            var description = JsonString(item, "description", "body", "content", "message");
            if (string.IsNullOrWhiteSpace(title)) title = tab;
            body.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = (Brush)Resources["ForegroundBrush"] });
            var metaLine = string.Join("  •  ", new[] { author, version, date }.Where(x => !string.IsNullOrWhiteSpace(x)));
            if (!string.IsNullOrWhiteSpace(metaLine))
                body.Children.Add(new TextBlock { Text = metaLine, Margin = new Thickness(0, 4, 0, 6), Foreground = (Brush)Resources["SecondaryBrush"] });
            if (!string.IsNullOrWhiteSpace(description))
                body.Children.Add(new TextBlock { Text = StripNexusMarkup(description), TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Resources["ForegroundBrush"] });
            card.Child = body;
            stack.Children.Add(card);
        }
        if (stack.Children.Count == 0)
            return EmptyNexusContent(L("No {0} are available.", tab));
        return stack;
    }

    private FrameworkElement BuildNexusDescriptionContent(string raw, bool offline = false)
    {
        try
        {
            if (offline)
            {
                // The cache stores the original Nexus markup, but remote images
                // cannot be downloaded in offline mode. Replace image blocks before
                // the normal BBCode parser sees them so no broken tags or URLs leak
                // into the cached view.
                raw = Regex.Replace(raw ?? "",
                    @"\[url(?:=[^\]]+)?\]\s*\[img(?:=[^\]]+)?\].*?\[/img\]\s*\[/url\]",
                    "Image Not Available In Offline Mode",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
                raw = Regex.Replace(raw,
                    @"\[img(?:=[^\]]+)?\].*?\[/img\]",
                    "Image Not Available In Offline Mode",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
                raw = Regex.Replace(raw,
                    @"<img\b[^>]*>",
                    "Image Not Available In Offline Mode",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
                raw = Regex.Replace(raw,
                    @"https?://(?:img\.shields\.io/|staticdelivery\.nexusmods\.com/)[^\s<>]+",
                    "Image Not Available In Offline Mode",
                    RegexOptions.IgnoreCase);
            }

            if (string.IsNullOrWhiteSpace(raw))
                return EmptyNexusContent(L("No description was provided."));

            var root = new StackPanel
            {
                Margin = new Thickness(4),
                Orientation = Orientation.Vertical
            };

            AppendNexusDescriptionBlocks(root, raw, TextAlignment.Left);

            return root.Children.Count == 0
                ? EmptyNexusContent(L("No description was provided."))
                : root;
        }
        catch (Exception ex)
        {
            CrashLogger.Write("NexusDescriptionContent", ex);
            return EmptyNexusContent(L("The Nexus description could not be displayed."));
        }
    }

    private string StripNexusMarkupSafe(string text)
    {
        try { return StripNexusMarkup(text); }
        catch { return System.Net.WebUtility.HtmlDecode(text ?? ""); }
    }

    private Brush NexusForegroundBrush()
    {
        try
        {
            if (TryFindResource("ForegroundBrush") is Brush brush) return brush;
            if (Foreground is Brush foreground) return foreground;
        }
        catch { }
        return Brushes.White;
    }

    private Brush NexusSecondaryBrush()
    {
        try
        {
            if (TryFindResource("SecondaryBrush") is Brush brush) return brush;
        }
        catch { }
        return NexusForegroundBrush();
    }

    private void AppendNexusDescriptionBlocks(Panel parent, string source, TextAlignment alignment = TextAlignment.Left)
    {
        try
        {
            source = NormalizeNexusMarkup(source);

            // Render structural Nexus blocks as native themed boxes. Process the
            // first block found so nested formatting inside it can use the normal
            // inline parser.
            var blockRegex = new Regex(@"\[(?<kind>spoiler|quote|code|list|center|left|right|justify)(?:=(?<arg>[^\]]+))?\](?<body>.*?)\[/\k<kind>\]",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var pos = 0;
            foreach (Match match in blockRegex.Matches(source))
            {
                if (match.Index > pos)
                    AppendNexusFormattedBlock(parent, source.Substring(pos, match.Index - pos), alignment);

                var kind = match.Groups["kind"].Value.ToLowerInvariant();
                var body = match.Groups["body"].Value;
                var arg = match.Groups["arg"].Success ? match.Groups["arg"].Value.Trim() : "";

                if (kind == "code")
                {
                    AppendNexusCodeBlock(parent, body);
                }
                else if (kind == "list")
                {
                    AppendNexusListBlock(parent, body, arg, alignment);
                }
                else if (kind == "center" || kind == "left" || kind == "right" || kind == "justify")
                {
                    var blockAlignment = kind switch
                    {
                        "center" => TextAlignment.Center,
                        "right" => TextAlignment.Right,
                        "justify" => TextAlignment.Justify,
                        _ => TextAlignment.Left
                    };
                    AppendNexusDescriptionBlocks(parent, body, blockAlignment);
                }
                else if (kind == "spoiler")
                {
                    var spoilerBody = new StackPanel { Margin = new Thickness(10, 6, 10, 10) };
                    AppendNexusDescriptionBlocks(spoilerBody, body, alignment);
                    var border = new Border
                    {
                        BorderBrush = NexusBorderBrush(),
                        BorderThickness = new Thickness(1),
                        Background = NexusPanelBrush(),
                        CornerRadius = new CornerRadius(6),
                        Margin = new Thickness(0, 6, 0, 6),
                        Padding = new Thickness(2)
                    };
                    var expander = new Expander
                    {
                        Header = new TextBlock
                        {
                            Text = L("Spoiler — click to reveal"),
                            FontWeight = FontWeights.SemiBold,
                            Foreground = NexusForegroundBrush()
                        },
                        Content = spoilerBody,
                        IsExpanded = false,
                        Foreground = NexusForegroundBrush()
                    };
                    border.Child = expander;
                    parent.Children.Add(border);
                }
                else
                {
                    var quoteBody = new StackPanel { Margin = new Thickness(10, 8, 10, 8) };
                    AppendNexusDescriptionBlocks(quoteBody, body, alignment);
                    parent.Children.Add(new Border
                    {
                        BorderBrush = NexusBorderBrush(),
                        BorderThickness = new Thickness(3, 1, 1, 1),
                        Background = NexusPanelBrush(),
                        CornerRadius = new CornerRadius(6),
                        Margin = new Thickness(0, 6, 0, 6),
                        Padding = new Thickness(8),
                        Child = quoteBody
                    });
                }

                pos = match.Index + match.Length;
            }

            if (pos < source.Length)
                AppendNexusFormattedBlock(parent, source.Substring(pos), alignment);
        }
        catch (Exception ex)
        {
            CrashLogger.Write("NexusDescriptionBlocks", ex);
            try
            {
                parent.Children.Add(new TextBlock
                {
                    Text = StripNexusMarkupSafe(source),
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = NexusForegroundBrush(),
                    Margin = new Thickness(2, 4, 2, 8),
                    FontSize = 14
                });
            }
            catch { }
        }
    }

    private Brush NexusBorderBrush()
    {
        try
        {
            if (TryFindResource("BorderBrush") is Brush brush) return brush;
            if (TryFindResource("ButtonBorderBrush") is Brush buttonBrush) return buttonBrush;
        }
        catch { }
        return NexusSecondaryBrush();
    }

    private Brush NexusPanelBrush()
    {
        try
        {
            if (TryFindResource("PanelBrush") is Brush brush) return brush;
            if (TryFindResource("BackgroundBrush") is Brush bg) return bg;
        }
        catch { }
        return Brushes.Transparent;
    }

    private static string ConvertNexusHtmlAlignment(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        // Convert common aligned paragraph/container forms one container at a time.
        // The inner content is left untouched so nested BBCode/HTML can be normalized
        // by the rest of the pipeline.
        var pattern = new Regex(
            @"<(?<tag>p|div|section|article)\b(?<attrs>[^>]*)>(?<body>.*?)</\k<tag>\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        return pattern.Replace(text, m =>
        {
            var attrs = m.Groups["attrs"].Value;
            var alignment =
                Regex.IsMatch(attrs, @"\balign\s*=\s*[""']?center\b", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(attrs, @"text-align\s*:\s*center", RegexOptions.IgnoreCase) ? "center" :
                Regex.IsMatch(attrs, @"\balign\s*=\s*[""']?right\b", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(attrs, @"text-align\s*:\s*right", RegexOptions.IgnoreCase) ? "right" :
                Regex.IsMatch(attrs, @"\balign\s*=\s*[""']?justify\b", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(attrs, @"text-align\s*:\s*justify", RegexOptions.IgnoreCase) ? "justify" :
                Regex.IsMatch(attrs, @"\balign\s*=\s*[""']?left\b", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(attrs, @"text-align\s*:\s*left", RegexOptions.IgnoreCase) ? "left" : null;

            return alignment == null
                ? m.Value
                : $"[{alignment}]{m.Groups["body"].Value}[/{alignment}]";
        });
    }

    private static string NormalizeNexusMarkup(string source)
    {
        var text = System.Net.WebUtility.HtmlDecode(source ?? "")
            .Replace("\r\n", "\n")
            .Replace("\r", "\n");

        // Convert common HTML returned alongside Nexus BBCode into equivalent
        // BBCode before parsing. Do not strip arbitrary HTML until this pass is done.
        text = Regex.Replace(text, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<\s*(strong|b)\s*>", "[b]", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<\s*/\s*(strong|b)\s*>", "[/b]", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<\s*(em|i)\s*>", "[i]", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<\s*/\s*(em|i)\s*>", "[/i]", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<\s*u\s*>", "[u]", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<\s*/\s*u\s*>", "[/u]", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<\s*(s|strike)\s*>", "[s]", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<\s*/\s*(s|strike)\s*>", "[/s]", RegexOptions.IgnoreCase);
        // Preserve alignment from the Nexus HTML itself. Some descriptions use
        // style="text-align:center" or align="center" instead of BBCode [center].
        // Translate aligned containers as paired blocks before stripping HTML.
        text = ConvertNexusHtmlAlignment(text);
        text = Regex.Replace(text, @"<\s*center\s*>", "[center]", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<\s*/\s*center\s*>", "[/center]", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<\s*(p|div|section|article)(?:\s+[^>]*)?>", "\n\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<\s*/\s*(p|div|section|article)\s*>", "\n\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<\s*hr\s*/?>", "[hr]", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<\s*li(?:\s+[^>]*)?>", "[*] ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<\s*/\s*li\s*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<\s*(ul|ol)(?:\s+[^>]*)?>", "[list]\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<\s*/\s*(ul|ol)\s*>", "[/list]\n", RegexOptions.IgnoreCase);

        // Anchor tags: retain the target rather than discarding useful links.
        text = Regex.Replace(text,
            @"<a\s+[^>]*href\s*=\s*(?:""([^""]+)""|\'([^\']+)\')[^>]*>",
            m => "[url=" + (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value) + "]",
            RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<\s*/\s*a\s*>", "[/url]", RegexOptions.IgnoreCase);

        // Preserve real image sources, including Nexus lazy-loaded images and
        // protocol-relative CDN URLs. Nexus commonly emits data-src/srcset rather
        // than a directly usable src in descriptions.
        text = Regex.Replace(text,
            @"<img\s+[^>]*(?:src|data-src|data-original|data-lazy-src)\s*=\s*(?:""([^""]+)""|\'([^\']+)\')[^>]*>",
            m =>
            {
                var url = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                if (url.StartsWith("//", StringComparison.Ordinal)) url = "https:" + url;
                return "[img=" + url + "]";
            },
            RegexOptions.IgnoreCase);

        // If the HTML only provides a srcset, use its first candidate.
        text = Regex.Replace(text,
            @"<img\s+[^>]*srcset\s*=\s*(?:""([^""]+)""|\'([^\']+)\')[^>]*>",
            m =>
            {
                var srcset = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                var url = srcset.Split(',')[0].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                if (url.StartsWith("//", StringComparison.Ordinal)) url = "https:" + url;
                return string.IsNullOrWhiteSpace(url) ? "" : "[img=" + url + "]";
            },
            RegexOptions.IgnoreCase);

        // Normalize Nexus's standard [img]URL[/img] form BEFORE converting bare
        // image URLs. Otherwise the bare-URL pass can see the URL inside the
        // [img] tag and turn it into nested [img] markup, which leaves stray ']'
        // characters and prevents the image token from being parsed correctly.
        text = Regex.Replace(text,
            @"\[img\]\s*(https?://[^\r\n\[]+?)\s*\[/img\]",
            m => "[img=" + m.Groups[1].Value.Trim() + "]",
            RegexOptions.IgnoreCase);

        // Nexus also auto-renders some bare image URLs (notably shields.io badges
        // and its own static-delivery CDN). Reproduce that behavior when the API
        // returns the URL as plain text rather than [img] markup. Do not match a
        // URL immediately following '[' or ']' because that is normally the body
        // of an existing BBCode tag such as [img]... or [url]....
        text = Regex.Replace(text,
            @"(?<![=""'/\[\]])(https?://(?:img\.shields\.io/|staticdelivery\.nexusmods\.com/)[^\s<>""']+)",
            "[img=$1]",
            RegexOptions.IgnoreCase);

        // Remove only remaining HTML tags. The useful structural tags have already
        // been translated above.
        text = Regex.Replace(text, @"<[^>]+>", "", RegexOptions.IgnoreCase);

        // Nexus aliases.
        text = Regex.Replace(text, @"\[font\s+size\s*=\s*([^\]]+)\]", "[size=$1]", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\[font\s+face\s*=\s*[^\]]+\]", "", RegexOptions.IgnoreCase);
        // Preserve Nexus center blocks so the native renderer can match the
        // alignment of the original Nexus description.
        text = Regex.Replace(text, @"\[center\]", "[center]", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\[/center\]", "[/center]", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\[/?(?:br|p)\]", "\n", RegexOptions.IgnoreCase);

        // Normalize both Nexus URL forms so the formatter can render one clickable
        // hyperlink instead of showing the destination beside the display text.
        text = Regex.Replace(text, @"\[url\]\s*(https?://[^\[]+)\s*\[/url\]", "[url=$1]$1[/url]", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\[url\]\s*([^\[]+?)\s*\[/url\]", "[url=$1]$1[/url]", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\[url\s*=\s*([^\]]+)\]", "[url=$1]", RegexOptions.IgnoreCase);
        // Remove only orphan image closing markers left by malformed input.
        text = Regex.Replace(text, @"\[/img\]", "", RegexOptions.IgnoreCase);
        return text;
    }

    private void AppendNexusCodeBlock(Panel parent, string source)
    {
        try
        {
            var code = System.Net.WebUtility.HtmlDecode(source ?? "")
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Trim('\n');

            var codeBrush = new SolidColorBrush(GetThemeColor("CodeBackgroundBrush", Color.FromRgb(45, 45, 45)));
            var codeForeground = new SolidColorBrush(GetThemeColor("CodeForegroundBrush", Colors.White));
            var borderBrush = NexusBorderBrush();

            var text = new TextBlock
            {
                Text = code,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                Foreground = codeForeground,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(12, 10, 12, 10)
            };

            var copy = new Button
            {
                Content = L("Copy"),
                Style = TryFindResource("BrowseButtonStyle") as Style,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(8)
            };
            copy.Click += (_, _) =>
            {
                try { Clipboard.SetText(code); }
                catch (Exception ex) { CrashLogger.Write("NexusCodeCopy", ex); }
            };

            var content = new Grid();
            content.Children.Add(text);
            content.Children.Add(copy);

            parent.Children.Add(new Border
            {
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                Background = codeBrush,
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 8, 0, 8),
                Padding = new Thickness(2),
                Child = content
            });
        }
        catch (Exception ex)
        {
            CrashLogger.Write("NexusCodeBlock", ex);
            AppendNexusFormattedBlock(parent, source);
        }
    }

    private void AppendNexusListBlock(Panel parent, string source, string listType, TextAlignment alignment = TextAlignment.Left)
    {
        try
        {
            var normalized = System.Net.WebUtility.HtmlDecode(source ?? "")
                .Replace("\r\n", "\n")
                .Replace("\r", "\n");

            var itemRegex = new Regex(@"(?:^|\n)\[\*\]\s*(?<item>.*?)(?=(?:\n\[\*\])|$)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var matches = itemRegex.Matches(normalized);
            if (matches.Count == 0)
            {
                var lines = normalized.Split('\n')
                    .Select(x => x.Trim())
                    .Where(x => x.Length > 0)
                    .ToArray();
                foreach (var line in lines)
                    AddNexusListItem(parent, line, "•", alignment);
                return;
            }

            var ordered = !string.IsNullOrWhiteSpace(listType) &&
                          !string.Equals(listType, "*", StringComparison.OrdinalIgnoreCase) &&
                          !string.Equals(listType, "#", StringComparison.OrdinalIgnoreCase);
            var index = 1;
            foreach (Match match in matches)
            {
                var marker = ordered ? index.ToString(CultureInfo.InvariantCulture) + "." : "•";
                AddNexusListItem(parent, match.Groups["item"].Value.Trim(), marker, alignment);
                index++;
            }
        }
        catch (Exception ex)
        {
            CrashLogger.Write("NexusListBlock", ex);
            AppendNexusFormattedBlock(parent, source);
        }
    }

    private void AddNexusListItem(Panel parent, string item, string marker, TextAlignment alignment = TextAlignment.Left)
    {
        var row = new Grid { Margin = new Thickness(8, 3, 8, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var markerText = new TextBlock
        {
            Text = marker,
            Foreground = NexusSecondaryBrush(),
            FontWeight = FontWeights.SemiBold,
            FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(markerText, 0);
        row.Children.Add(markerText);

        var body = new StackPanel { Orientation = Orientation.Vertical };
        Grid.SetColumn(body, 1);

        // List items can contain block-level BBCode such as [code], [quote],
        // and [spoiler]. Use the block-aware description renderer here so those
        // tags are rendered as their proper UI instead of being left as text.
        AppendNexusDescriptionBlocks(body, item, alignment);
        row.Children.Add(body);

        parent.Children.Add(row);
    }

    private void AppendNexusFormattedBlock(Panel parent, string source, TextAlignment alignment = TextAlignment.Left)
    {
        if (string.IsNullOrWhiteSpace(source)) return;

        try
        {
            source = NormalizeNexusMarkup(source);
            var paragraph = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = alignment,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(2, 4, 2, 8),
                Foreground = NexusForegroundBrush(),
                FontSize = 16
            };

            var root = new Span();
            paragraph.Inlines.Add(root);
            var stack = new Stack<(string Tag, Span Span)>();
            Span current = root;

            void AddText(string value)
            {
                if (string.IsNullOrEmpty(value)) return;
                value = System.Net.WebUtility.HtmlDecode(value);
                if (value.Length == 0) return;

                var parts = value.Split('\n');
                for (var i = 0; i < parts.Length; i++)
                {
                    if (parts[i].Length > 0)
                        current.Inlines.Add(new Run(parts[i]));
                    if (i < parts.Length - 1)
                        current.Inlines.Add(new LineBreak());
                }
            }

            void OpenSpan(string tag, string value)
            {
                var span = new Span();
                switch (tag)
                {
                    case "b": case "strong": span.FontWeight = FontWeights.Bold; break;
                    case "i": case "em": span.FontStyle = FontStyles.Italic; break;
                    case "u": span.TextDecorations = TextDecorations.Underline; break;
                    case "s": case "strike": span.TextDecorations = TextDecorations.Strikethrough; break;
                    case "size": case "font":
                        if (TryNexusFontSize(value, out var size)) span.FontSize = size;
                        break;
                    case "color":
                        try
                        {
                            span.Foreground = NexusSemanticColorBrush(value);
                        }
                        catch { }
                        break;
                }
                current.Inlines.Add(span);
                stack.Push((tag, span));
                current = span;
            }

            void CloseSpan(string tag)
            {
                if (stack.Count == 0) return;
                while (stack.Count > 0)
                {
                    var item = stack.Pop();
                    if (string.Equals(item.Tag, tag, StringComparison.OrdinalIgnoreCase))
                    {
                        current = stack.Count > 0 ? stack.Peek().Span : root;
                        return;
                    }
                }
                current = root;
            }

            var tokenRegex = new Regex(
                @"\[(?<close>/)?(?<name>b|strong|i|em|u|s|strike|size|color|font|url|img|hr|\*)(?:=(?<value>[^\]]+))?\]",
                RegexOptions.IgnoreCase);

            var index = 0;
            foreach (Match match in tokenRegex.Matches(source))
            {
                AddText(source.Substring(index, match.Index - index));
                var name = match.Groups["name"].Value.ToLowerInvariant();
                var closing = match.Groups["close"].Success;
                var value = match.Groups["value"].Success
                    ? match.Groups["value"].Value.Trim().Trim('"', '\'')
                    : "";

                if (name == "hr" && !closing)
                {
                    current.Inlines.Add(new LineBreak());
                    current.Inlines.Add(new Run("────────────────────────────────────────"));
                    current.Inlines.Add(new LineBreak());
                }
                else if (name == "*" && !closing)
                {
                    AddText("• ");
                }
                else if ((name == "url" || name == "img") && !closing)
                {
                    var target = value;
                    if (string.IsNullOrWhiteSpace(target))
                    {
                        // [url]...[/url] was normalized above when its body is a URL.
                        // If an unusual empty URL tag remains, leave it harmlessly blank.
                        index = match.Index + match.Length;
                        continue;
                    }

                    if (name == "img")
                    {
                        if (target.StartsWith("//", StringComparison.Ordinal)) target = "https:" + target;
                        if (Uri.TryCreate(target, UriKind.Absolute, out var imageUri) &&
                            (imageUri.Scheme == Uri.UriSchemeHttp || imageUri.Scheme == Uri.UriSchemeHttps))
                        {
                            // Keep the image in an InlineUIContainer so it can also
                            // live inside a [url]...[/url] hyperlink. A Border is used
                            // as a replaceable placeholder because SVG cannot be loaded
                            // by WPF's BitmapImage.
                            var placeholder = new Border
                            {
                                Padding = new Thickness(0),
                                Margin = new Thickness(4, 8, 4, 8),
                                HorizontalAlignment = HorizontalAlignment.Center,
                                Cursor = System.Windows.Input.Cursors.Hand,
                                ToolTip = target
                            };
                            placeholder.MouseLeftButtonUp += (_, _) => OpenUrl(target);
                            var inline = new InlineUIContainer(placeholder);
                            current.Inlines.Add(inline);
                            _ = LoadNexusImageAsync(inline, placeholder, target);
                        }
                        else if (!string.IsNullOrWhiteSpace(target))
                        {
                            AddText(target);
                        }
                        index = match.Index + match.Length;
                        continue;
                    }

                    // Hyperlink derives from Span, so it can remain on the same
                    // formatting stack until [/url]. The visible text between the
                    // tags is therefore the link text, not a second plain URL.
                    var hyperlink = new Hyperlink
                    {
                        Foreground = NexusSecondaryBrush(),
                        TextDecorations = TextDecorations.Underline
                    };
                    hyperlink.Click += (_, _) => OpenUrl(target);
                    current.Inlines.Add(hyperlink);
                    stack.Push(("url", hyperlink));
                    current = hyperlink;
                }
                else if (closing)
                {
                    if (name != "img")
                        CloseSpan(name);
                }
                else
                {
                    OpenSpan(name, value);
                }

                index = match.Index + match.Length;
            }

            AddText(source.Substring(index));

            // Unclosed formatting tags are harmless: their Span simply ends here.
            if (paragraph.Inlines.Count > 0)
                parent.Children.Add(paragraph);
        }
        catch (Exception ex)
        {
            CrashLogger.Write("NexusDescriptionFormattedBlock", ex);
            try
            {
                parent.Children.Add(new TextBlock
                {
                    Text = StripNexusMarkupSafe(source),
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = NexusForegroundBrush(),
                    Margin = new Thickness(2, 4, 2, 8),
                    FontSize = 14
                });
            }
            catch { }
        }
    }

    private async Task LoadNexusImageAsync(InlineUIContainer inline, Border placeholder, string target)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 RetroRewind/1.0");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://www.nexusmods.com/");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8");

            using var response = await client.GetAsync(target, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes.Length == 0) throw new InvalidDataException("Empty image response.");

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            var isSvg = contentType.Contains("svg", StringComparison.OrdinalIgnoreCase);
            if (!isSvg)
            {
                var probe = System.Text.Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 512));
                isSvg = probe.TrimStart().StartsWith("<svg", StringComparison.OrdinalIgnoreCase) ||
                        probe.Contains("<svg", StringComparison.OrdinalIgnoreCase);
            }

            await Dispatcher.InvokeAsync(() =>
            {
                if (isSvg)
                {
                    var svg = new SvgViewbox
                    {
                        AutoSize = true,
                        Stretch = Stretch.Uniform,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        MaxWidth = 760,
                        MaxHeight = 900,
                        ToolTip = target
                    };
                    svg.Load(new MemoryStream(bytes), true, true);
                    svg.MouseLeftButtonUp += (_, _) => OpenUrl(target);
                    placeholder.Child = svg;
                }
                else
                {
                    using var stream = new MemoryStream(bytes);
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    var image = new Image
                    {
                        Source = bitmap,
                        MaxWidth = 760,
                        MaxHeight = 900,
                        Stretch = Stretch.Uniform,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Cursor = System.Windows.Input.Cursors.Hand
                    };
                    image.MouseLeftButtonUp += (_, _) => OpenUrl(target);
                    placeholder.Child = image;
                }
            });
        }
        catch (Exception ex)
        {
            CrashLogger.Write("NexusDescriptionImageLoad", ex);
            await Dispatcher.InvokeAsync(() =>
            {
                // Do not leave a stray BBCode bracket/tag on screen when a remote
                // image cannot be decoded. Keep the description clean and clickable.
                placeholder.Child = new TextBlock
                {
                    Text = "[Image unavailable]",
                    Foreground = NexusSecondaryBrush(),
                    FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Cursor = System.Windows.Input.Cursors.Hand
                };
            });
        }
    }

    private Brush NexusSemanticColorBrush(string nexusColor)
    {
        // Nexus descriptions can request arbitrary colors. Do not render those
        // raw values directly: bright reds/greens/blues from a Nexus page can
        // clash badly with the selected Retro Rewind palette. Instead, classify
        // the author's requested color by intent/hue and generate a compatible
        // color from the current theme's accent.
        try
        {
            if (!TryParseNexusColor(nexusColor, out var source))
                return NexusForegroundBrush();

            if (source.A < 220)
                return NexusForegroundBrush();

            // Near-white/near-black Nexus colors become theme foreground/muted
            // colors so they remain readable in both light and dark palettes.
            var sourceBrightness = (0.299 * source.R + 0.587 * source.G + 0.114 * source.B) / 255.0;
            var sourceSaturation = NexusColorSaturation(source);
            if (sourceSaturation < 0.12)
                return sourceBrightness > 0.72 ? NexusForegroundBrush() : NexusSecondaryBrush();

            var accent = GetThemeColor("AccentBrush", Color.FromRgb(214, 90, 49));
            var background = GetThemeColor("WindowBackgroundBrush", Color.FromRgb(244, 235, 208));
            var accentHsl = RgbToHsl(accent);
            var sourceHsl = RgbToHsl(source);

            // Preserve the author's broad color intent while moving the hue into
            // the application's accent family. The offsets keep red/orange,
            // yellow, green, cyan/blue and purple/magenta visually distinct.
            var offset = NexusSemanticHueOffset(sourceHsl.H);
            var hue = NormalizeHue(accentHsl.H + offset);
            var saturation = Math.Clamp(Math.Max(0.28, accentHsl.S * 0.72), 0.28, 0.78);

            var backgroundLuma = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255.0;
            var lightness = backgroundLuma < 0.48 ? 0.68 : 0.38;

            // Very bright source colors are treated as emphasis/highlight; keep
            // them a little brighter without ever returning the raw Nexus value.
            if (sourceBrightness > 0.78)
                lightness = backgroundLuma < 0.48 ? 0.76 : 0.34;

            var mapped = HslToColor(hue, saturation, lightness);
            return new SolidColorBrush(mapped);
        }
        catch
        {
            return NexusForegroundBrush();
        }
    }

    private static double NexusSemanticHueOffset(double sourceHue)
    {
        // Normalize the source hue into broad semantic buckets. These are not
        // exact Nexus colors; they are only used to retain the author's intent.
        if (sourceHue < 25 || sourceHue >= 345) return -8;      // red/danger
        if (sourceHue < 55) return 18;                          // orange/warning
        if (sourceHue < 90) return 38;                          // yellow/highlight
        if (sourceHue < 165) return 105;                        // green/success
        if (sourceHue < 205) return 165;                        // cyan/info
        if (sourceHue < 255) return 195;                        // blue/info
        if (sourceHue < 305) return 245;                        // purple/secondary
        return 300;                                             // magenta/secondary
    }

    private static bool TryParseNexusColor(string value, out Color color)
    {
        color = Colors.Transparent;
        if (string.IsNullOrWhiteSpace(value)) return false;
        value = value.Trim().Trim('"', '\'');

        try
        {
            if (value.StartsWith("#", StringComparison.Ordinal))
            {
                if (value.Length == 7 && byte.TryParse(value.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
                    && byte.TryParse(value.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
                    && byte.TryParse(value.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
                {
                    color = Color.FromRgb(r, g, b);
                    return true;
                }
                if (value.Length == 9 && byte.TryParse(value.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var a)
                    && byte.TryParse(value.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rr)
                    && byte.TryParse(value.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var gg)
                    && byte.TryParse(value.Substring(7, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var bb))
                {
                    color = Color.FromArgb(a, rr, gg, bb);
                    return true;
                }
            }

            if (new BrushConverter().ConvertFromString(value) is SolidColorBrush brush)
            {
                color = brush.Color;
                return true;
            }
        }
        catch { }
        return false;
    }

    private Color GetThemeColor(string resourceKey, Color fallback)
    {
        try
        {
            if (TryFindResource(resourceKey) is SolidColorBrush brush)
                return brush.Color;
        }
        catch { }
        return fallback;
    }

    private static double NexusColorSaturation(Color color)
    {
        return RgbToHsl(color).S;
    }

    private static NexusHsl RgbToHsl(Color color)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2.0;
        if (Math.Abs(max - min) < 0.00001) return new NexusHsl(0, 0, l);

        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h;
        if (Math.Abs(max - r) < 0.00001)
            h = ((g - b) / d + (g < b ? 6 : 0)) * 60;
        else if (Math.Abs(max - g) < 0.00001)
            h = ((b - r) / d + 2) * 60;
        else
            h = ((r - g) / d + 4) * 60;
        return new NexusHsl(h, s, l);
    }

    private static Color HslToColor(double h, double s, double l)
    {
        h = NormalizeHue(h) / 360.0;
        double r, g, b;
        if (s <= 0.00001)
        {
            r = g = b = l;
        }
        else
        {
            var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;
            r = HueToRgb(p, q, h + 1.0 / 3.0);
            g = HueToRgb(p, q, h);
            b = HueToRgb(p, q, h - 1.0 / 3.0);
        }
        return Color.FromRgb((byte)Math.Clamp((int)Math.Round(r * 255), 0, 255),
                             (byte)Math.Clamp((int)Math.Round(g * 255), 0, 255),
                             (byte)Math.Clamp((int)Math.Round(b * 255), 0, 255));
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2.0) return q;
        if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
        return p;
    }

    private static double NormalizeHue(double hue)
    {
        hue %= 360;
        if (hue < 0) hue += 360;
        return hue;
    }

    private static bool TryNexusFontSize(string value, out double size)
    {
        size = 14;
        if (string.IsNullOrWhiteSpace(value)) return false;
        value = value.Trim().Trim('"', '\'');
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
            return false;
        size = Math.Clamp(n <= 7 ? 10 + n * 4 : n, 10, 42);
        return true;
    }

    private FrameworkElement EmptyNexusContent(string text) =>
        new TextBlock { Text = text, Foreground = NexusSecondaryBrush(), Margin = new Thickness(8), FontSize = 14 };

    private sealed record NexusFileDownloadRequest(string Game, int ModId, int FileId, string FileName, string Version, int CurrentFileCount = 1, string? OneTimeKey = null, string? Expires = null, string? UserId = null);

    private static int JsonInt(JsonElement item, params string[] names)
    {
        if (item.ValueKind != JsonValueKind.Object) return 0;
        foreach (var name in names)
        {
            if (!item.TryGetProperty(name, out var value)) continue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number)) return number;
        }
        return 0;
    }

    private static string FormatNexusFileSize(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("size_in_bytes", out var value)) return "";
        long bytes = 0;
        if (value.ValueKind == JsonValueKind.Number) value.TryGetInt64(out bytes);
        else if (value.ValueKind == JsonValueKind.String) long.TryParse(value.GetString(), out bytes);
        if (bytes <= 0) return "";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        var unit = 0;
        double size = bytes;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:0.##} {units[unit]}";
    }

    private string? FindNexusDownloadedFile(string game, int modId, int fileId)
    {
        if (modId <= 0 || fileId <= 0) return null;
        var metadata = LoadNexusMetadata();
        foreach (var entry in metadata.Values)
        {
            if (entry.ModId != modId || !string.Equals(entry.Game, game, StringComparison.OrdinalIgnoreCase) || entry.FileId != fileId || string.IsNullOrWhiteSpace(entry.ArchivePath)) continue;
            var path = Path.Combine(GetDownloadsDirectory(), entry.ArchivePath);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    private async Task<int> FetchNexusCurrentFileCountAsync(string game, int modId)
    {
        if (string.IsNullOrWhiteSpace(game) || modId <= 0) return -1;
        var apiKey = string.IsNullOrWhiteSpace(_nexusApiKey) ? NexusSecretStore.Load() : _nexusApiKey;
        if (string.IsNullOrWhiteSpace(apiKey)) return -1;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RetroRewindModHub/1.0.0");
        client.DefaultRequestHeaders.TryAddWithoutValidation("apikey", apiKey);
        var url = $"https://api.nexusmods.com/v1/games/{Uri.EscapeDataString(game)}/mods/{modId}/files.json";
        using var response = await client.GetAsync(url);
        if (!response.IsSuccessStatusCode) return -1;
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement.EnumerateArray().ToList()
            : doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array
                ? files.EnumerateArray().ToList()
                : new List<JsonElement>();
        var count = 0;
        foreach (var item in items)
        {
            var category = JsonString(item, "category_name", "category", "categoryName").Trim();
            if (category.Equals("old", StringComparison.OrdinalIgnoreCase) || category.Equals("old file", StringComparison.OrdinalIgnoreCase) ||
                category.Equals("archived", StringComparison.OrdinalIgnoreCase) || category.Equals("archive", StringComparison.OrdinalIgnoreCase))
                continue;
            count++;
        }
        return count;
    }

    private async Task<string> GetNexusPremiumStatusAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        if (DateTime.UtcNow - _nexusAccountStatusCheckedUtc < TimeSpan.FromMinutes(10) && !string.Equals(_nexusAccountPremiumStatus, "Unknown", StringComparison.OrdinalIgnoreCase))
            return _nexusAccountPremiumStatus;
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Retro Rewind ModHub/1.0.11");
            client.DefaultRequestHeaders.TryAddWithoutValidation("apikey", apiKey);
            using var response = await client.GetAsync("https://api.nexusmods.com/v1/users/me.json", cancellationToken);
            if (!response.IsSuccessStatusCode) return _nexusAccountPremiumStatus;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = doc.RootElement;
            var premium = root.TryGetProperty("is_premium", out var p) && p.ValueKind == JsonValueKind.True;
            _nexusAccountPremiumStatus = premium ? "Premium" : "Non-Premium";
            _nexusAccountStatusCheckedUtc = DateTime.UtcNow;
        }
        catch { }
        return _nexusAccountPremiumStatus;
    }

    private void NotifyDownload(string title, string message)
    {
        if (!_enableWindowsNotifications || _trayIcon == null) return;
        try { _trayIcon.ShowBalloonTip(3500, title, message, Forms.ToolTipIcon.Info); } catch { }
    }

    private void UpdateActiveDownload(ActiveDownloadState state, long downloadedBytes, long totalBytes)
    {
        var now = DateTime.UtcNow;
        lock (_activeDownloadsSync)
        {
            state.DownloadedBytes = downloadedBytes;
            state.TotalBytes = totalBytes;
            var elapsed = (now - state.LastSampleUtc).TotalSeconds;
            if (elapsed >= 0.25)
            {
                var delta = downloadedBytes - state.LastSampleBytes;
                var instant = delta / elapsed;
                state.BytesPerSecond = state.BytesPerSecond <= 0 ? instant : (state.BytesPerSecond * 0.75) + (instant * 0.25);
                state.LastSampleBytes = downloadedBytes;
                state.LastSampleUtc = now;
            }
            if (_enableWindowsNotifications && now - state.LastNotificationUtc >= TimeSpan.FromSeconds(15))
            {
                var percent = totalBytes > 0 ? downloadedBytes * 100.0 / totalBytes : 0;
                NotifyDownload(L("Downloading {0}", state.NexusModName),
                    totalBytes > 0
                        ? L("{0:0}% • {1} • {2}/s", percent, FormatDownloadSize(downloadedBytes), FormatDownloadSpeed(state.BytesPerSecond))
                        : L("{0} • {1}/s", FormatDownloadSize(downloadedBytes), FormatDownloadSpeed(state.BytesPerSecond)));
                state.LastNotificationUtc = now;
            }
        }
        if (state.IsBootstrapUe4ss && totalBytes > 0)
        {
            var percent = downloadedBytes * 100.0 / totalBytes;
            SetOperationBusy(true, L("Downloading UE4SS…"), percent, L("{0} / {1} • {2}/s", FormatDownloadSize(downloadedBytes), FormatDownloadSize(totalBytes), FormatDownloadSpeed(state.BytesPerSecond)));
        }
        if (_mode == "downloads")
            Dispatcher.BeginInvoke(new Action(RefreshDownloadsPage));
    }

    private async Task DownloadNexusFileAsync(NexusFileDownloadRequest request)
    {
        if (request.FileId <= 0) throw new InvalidOperationException(L("Nexus did not provide a valid file ID."));
        var apiKey = string.IsNullOrWhiteSpace(_nexusApiKey) ? NexusSecretStore.Load() : _nexusApiKey;
        var hasOneTimeKey = !string.IsNullOrWhiteSpace(request.OneTimeKey);
        if (string.IsNullOrWhiteSpace(apiKey) && !hasOneTimeKey)
            throw new InvalidOperationException(L("Connect to Nexus in Settings to download files, or start the download from Nexus Mods using Download with Mod Manager."));

        var premiumStatus = string.IsNullOrWhiteSpace(apiKey) ? "Unknown" : await GetNexusPremiumStatusAsync(apiKey);
        string nexusModName = string.IsNullOrWhiteSpace(request.FileName) ? $"Nexus Mod {request.ModId}" : request.FileName;
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            try
            {
                var modInfo = await FetchNexusModInfoAsync(request.Game, request.ModId);
                if (modInfo != null && !string.IsNullOrWhiteSpace(modInfo.Name)) nexusModName = modInfo.Name;
            }
            catch { }
        }
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Retro Rewind ModHub/1.0.11");
        client.DefaultRequestHeaders.TryAddWithoutValidation("apikey", apiKey);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");

        var url = $"https://api.nexusmods.com/v1/games/{Uri.EscapeDataString(request.Game)}/mods/{request.ModId}/files/{request.FileId}/download_link.json";
        var queryParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.OneTimeKey)) queryParts.Add("key=" + Uri.EscapeDataString(request.OneTimeKey));
        if (!string.IsNullOrWhiteSpace(request.Expires)) queryParts.Add("expires=" + Uri.EscapeDataString(request.Expires));
        if (!string.IsNullOrWhiteSpace(request.UserId)) queryParts.Add("user_id=" + Uri.EscapeDataString(request.UserId));
        if (queryParts.Count > 0) url += "?" + string.Join("&", queryParts);
        using var response = await client.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            // Free Nexus accounts may require the normal web download flow instead
            // of an API-generated direct link. Do not attempt to bypass the
            // countdown/speed restrictions; hand the download to Nexus in the
            // user's browser and import the completed file into ModHub.
            if (hasOneTimeKey || premiumStatus.Equals("Non-Premium", StringComparison.OrdinalIgnoreCase) || response.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized)
            {
                await StartFreeNexusBrowserDownloadAsync(request, nexusModName, premiumStatus);
                return;
            }
            throw new InvalidOperationException(L("Nexus could not provide the download link (HTTP {0}).", (int)response.StatusCode));
        }
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        string? directUrl = null;
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var link in doc.RootElement.EnumerateArray())
            {
                if (link.ValueKind == JsonValueKind.Object && link.TryGetProperty("URI", out var uri) && uri.ValueKind == JsonValueKind.String)
                { directUrl = uri.GetString(); break; }
            }
        }
        if (string.IsNullOrWhiteSpace(directUrl))
        {
            if (hasOneTimeKey || premiumStatus.Equals("Non-Premium", StringComparison.OrdinalIgnoreCase))
            {
                await StartFreeNexusBrowserDownloadAsync(request, nexusModName, premiumStatus);
                return;
            }
            throw new InvalidOperationException(L("Nexus did not return a downloadable file link."));
        }

        var downloadDir = GetDownloadsDirectory();
        Directory.CreateDirectory(downloadDir);
        var safeName = string.Concat((string.IsNullOrWhiteSpace(request.FileName) ? $"NexusMod_{request.ModId}_{request.FileId}" : request.FileName).Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Trim();
        if (string.IsNullOrWhiteSpace(safeName)) safeName = $"NexusMod_{request.ModId}_{request.FileId}";
        var ext = Path.GetExtension(safeName);
        if (string.IsNullOrWhiteSpace(ext))
        {
            try { ext = Path.GetExtension(new Uri(directUrl).AbsolutePath); } catch { }
            if (string.IsNullOrWhiteSpace(ext) || ext.Length > 8) ext = ".zip";
            safeName += ext;
        }
        var archivePath = GetUniqueDownloadPath(Path.Combine(downloadDir, safeName));
        var partialPath = archivePath + ".download";
        var state = new ActiveDownloadState
        {
            Id = $"{request.Game}:{request.ModId}:{request.FileId}:{Guid.NewGuid():N}",
            NexusModName = nexusModName,
            FileName = Path.GetFileName(archivePath),
            Version = string.IsNullOrWhiteSpace(request.Version) ? "Unknown" : request.Version,
            Type = "PAK/UE4SS",
            DestinationPath = archivePath,
            StartedUtc = DateTime.UtcNow,
            LastSampleUtc = DateTime.UtcNow,
            LastSampleBytes = 0,
            PremiumStatus = premiumStatus,
            IsBootstrapUe4ss = request.Game.Equals("retrorewindvideostoresimulator", StringComparison.OrdinalIgnoreCase) && request.ModId == 52
        };
        lock (_activeDownloadsSync) _activeDownloads[state.Id] = state;
        NotifyDownload(L("Retro Rewind ModHub"), L("Started downloading {0}", state.NexusModName));
        try
        {
            using var download = await client.GetAsync(directUrl, HttpCompletionOption.ResponseHeadersRead);
            download.EnsureSuccessStatusCode();
            var totalBytes = download.Content.Headers.ContentLength ?? -1;
            await using var input = await download.Content.ReadAsStreamAsync();
            await using var output = File.Create(partialPath);
            var buffer = new byte[128 * 1024];
            long downloadedBytes = 0;
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read));
                downloadedBytes += read;
                UpdateActiveDownload(state, downloadedBytes, totalBytes);
            }
            await output.FlushAsync();
            output.Close();
            File.Move(partialPath, archivePath);

            var metadata = LoadNexusMetadata();
            var info = new NexusModMetadata(request.FileName, request.Game, request.ModId, request.FileId, Path.GetFileName(archivePath))
            { InstalledVersion = request.Version, LatestVersion = request.Version, DownloadedAtUtc = DateTime.UtcNow, NexusCurrentFileCount = request.CurrentFileCount };
            try
            {
                var modInfo = await FetchNexusModInfoAsync(request.Game, request.ModId);
                if (modInfo != null) info = ApplyNexusInfo(info, modInfo) with { InstalledVersion = request.Version };
            }
            catch { }
            metadata["_download:" + Path.GetFileName(archivePath)] = info;
            SaveNexusMetadata(metadata);
            lock (_activeDownloadsSync) _activeDownloads.Remove(state.Id);
            NotifyDownload(L("Download complete"), L("{0} has finished downloading.", state.NexusModName));
            InvalidateDownloadsCache();
            RefreshDownloadsPage();
            RefreshModManager();
        }
        catch
        {
            try { if (File.Exists(partialPath)) File.Delete(partialPath); } catch { }
            lock (_activeDownloadsSync) _activeDownloads.Remove(state.Id);
            NotifyDownload(L("Download failed"), L("{0} could not be downloaded.", state.NexusModName));
            throw;
        }
    }

    private async Task StartFreeNexusBrowserDownloadAsync(NexusFileDownloadRequest request, string nexusModName, string premiumStatus)
    {
        var browserDownloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        Directory.CreateDirectory(browserDownloads);
        var downloadDir = GetDownloadsDirectory();
        Directory.CreateDirectory(downloadDir);

        var expectedName = request.FileName?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(expectedName)) expectedName = $"NexusMod_{request.ModId}_{request.FileId}.zip";
        expectedName = string.Concat(expectedName.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Trim();
        if (string.IsNullOrWhiteSpace(expectedName)) expectedName = $"NexusMod_{request.ModId}_{request.FileId}.zip";
        if (request.Game.Equals("retrorewindvideostoresimulator", StringComparison.OrdinalIgnoreCase) && request.ModId == 52 && string.IsNullOrWhiteSpace(Path.GetExtension(expectedName)))
            expectedName += ".zip";

        var pageUrl = $"https://www.nexusmods.com/{Uri.EscapeDataString(request.Game)}/mods/{request.ModId}?tab=files&file_id={request.FileId}";
        var state = new ActiveDownloadState
        {
            Id = $"browser:{request.Game}:{request.ModId}:{request.FileId}:{Guid.NewGuid():N}",
            NexusModName = nexusModName,
            FileName = expectedName,
            Version = string.IsNullOrWhiteSpace(request.Version) ? "Unknown" : request.Version,
            Type = "PAK/UE4SS",
            DestinationPath = Path.Combine(downloadDir, expectedName),
            StartedUtc = DateTime.UtcNow,
            LastSampleUtc = DateTime.UtcNow,
            LastSampleBytes = 0,
            PremiumStatus = premiumStatus,
            IsBootstrapUe4ss = request.Game.Equals("retrorewindvideostoresimulator", StringComparison.OrdinalIgnoreCase) && request.ModId == 52
        };
        lock (_activeDownloadsSync) _activeDownloads[state.Id] = state;

        try
        {
            SetOperationBusy(true, L("Waiting for Nexus download…"), null, L("Nexus is handling the free-user countdown. Complete the download in your browser."));
            NotifyDownload(L("Nexus download"), L("Nexus is ready to download {0}. Complete the free download in your browser.", nexusModName));
            Process.Start(new ProcessStartInfo(pageUrl) { UseShellExecute = true });

            var startedUtc = DateTime.UtcNow;
            string? sourcePath = null;
            long lastSize = -1;
            DateTime lastChange = DateTime.UtcNow;
            for (var elapsed = TimeSpan.Zero; elapsed < TimeSpan.FromMinutes(30); elapsed += TimeSpan.FromSeconds(1))
            {
                await Task.Delay(1000);
                var progressPath = FindBrowserDownloadProgressFile(browserDownloads, expectedName, startedUtc);
                var completedPath = FindBrowserDownloadFile(browserDownloads, expectedName, startedUtc);
                if (progressPath == null && completedPath == null) continue;
                var observedPath = progressPath ?? completedPath!;
                long size;
                try { size = new FileInfo(observedPath).Length; } catch { continue; }
                if (size != lastSize) { lastSize = size; lastChange = DateTime.UtcNow; }
                UpdateActiveDownload(state, size, -1);
                SetOperationBusy(true, L("Downloading through Nexus…"), null, L("{0} downloaded • waiting for Nexus to finish…", FormatDownloadSize(size)));
                if (completedPath == null) continue;
                if (DateTime.UtcNow - lastChange < TimeSpan.FromSeconds(3)) continue;
                try
                {
                    using var probe = new FileStream(completedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (probe.Length <= 0) continue;
                    sourcePath = completedPath;
                }
                catch { continue; }
                break;
            }

            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                throw new TimeoutException(L("The Nexus free download was not detected in your Windows Downloads folder. If your browser saves downloads somewhere else, move the completed file into ModHub's Downloads folder and use Query Info."));

            var finalName = Path.GetFileName(sourcePath);
            if (state.IsBootstrapUe4ss && string.IsNullOrWhiteSpace(Path.GetExtension(finalName)))
                finalName += ".zip";
            var finalPath = GetUniqueDownloadPath(Path.Combine(downloadDir, finalName));
            SetOperationBusy(true, L("Importing Nexus download…"), null, finalName);
            await CopyFileWithProgressAsync(sourcePath, finalPath, state);

            var metadata = LoadNexusMetadata();
            var info = new NexusModMetadata(nexusModName, request.Game, request.ModId, request.FileId, Path.GetFileName(finalPath))
            {
                InstalledVersion = request.Version,
                LatestVersion = request.Version,
                DownloadedAtUtc = DateTime.UtcNow,
                NexusCurrentFileCount = request.CurrentFileCount
            };
            try
            {
                var modInfo = await FetchNexusModInfoAsync(request.Game, request.ModId);
                if (modInfo != null) info = ApplyNexusInfo(info, modInfo) with { InstalledVersion = request.Version };
            }
            catch { }
            metadata["_download:" + Path.GetFileName(finalPath)] = info;
            SaveNexusMetadata(metadata);
            NotifyDownload(L("Download complete"), L("{0} has been imported into ModHub Downloads.", nexusModName));
            InvalidateDownloadsCache();
            RefreshDownloadsPage();
            RefreshModManager();
        }
        finally
        {
            lock (_activeDownloadsSync) _activeDownloads.Remove(state.Id);
            SetOperationBusy(false);
            if (_mode == "downloads") Dispatcher.BeginInvoke(new Action(RefreshDownloadsPage));
        }
    }

    private static string? FindBrowserDownloadProgressFile(string folder, string expectedName, DateTime startedUtc)
    {
        try
        {
            var expectedBase = Path.GetFileNameWithoutExtension(expectedName);
            var allowedExtensions = new[] { ".crdownload", ".part", ".tmp" };
            var candidates = Directory.EnumerateFiles(folder)
                .Where(p => allowedExtensions.Any(ext => p.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                .Where(p => File.GetLastWriteTimeUtc(p) >= startedUtc.AddSeconds(-30))
                .OrderByDescending(p => File.GetLastWriteTimeUtc(p));

            foreach (var candidate in candidates)
            {
                var name = Path.GetFileName(candidate);
                var withoutPartial = name;
                foreach (var ext in allowedExtensions)
                {
                    if (withoutPartial.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    {
                        withoutPartial = withoutPartial[..^ext.Length];
                        break;
                    }
                }
                var candidateBase = Path.GetFileNameWithoutExtension(withoutPartial);
                if (string.Equals(candidateBase, expectedBase, StringComparison.OrdinalIgnoreCase) ||
                    candidateBase.StartsWith(expectedBase + " (", StringComparison.OrdinalIgnoreCase) ||
                    candidateBase.StartsWith(expectedBase + "-", StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }
        }
        catch { }
        return null;
    }

    private static string? FindBrowserDownloadFile(string folder, string expectedName, DateTime startedUtc)
    {
        try
        {
            var expectedBase = Path.GetFileNameWithoutExtension(expectedName);
            var exact = Path.Combine(folder, expectedName);
            if (File.Exists(exact) && File.GetLastWriteTimeUtc(exact) >= startedUtc.AddSeconds(-30)) return exact;

            var candidates = Directory.EnumerateFiles(folder)
                .Where(p => !p.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase) &&
                            !p.EndsWith(".part", StringComparison.OrdinalIgnoreCase) &&
                            !p.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                .Where(p => File.GetLastWriteTimeUtc(p) >= startedUtc.AddSeconds(-30))
                .OrderByDescending(p => File.GetLastWriteTimeUtc(p));

            foreach (var candidate in candidates)
            {
                var candidateBase = Path.GetFileNameWithoutExtension(candidate);
                if (string.Equals(candidateBase, expectedBase, StringComparison.OrdinalIgnoreCase) ||
                    candidateBase.StartsWith(expectedBase + " (", StringComparison.OrdinalIgnoreCase) ||
                    candidateBase.StartsWith(expectedBase + "-", StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }

            // An nxm:// request may not contain the original archive filename.
            // If Nexus handed the download to the browser, accept the newest
            // completed archive created after this request started rather than
            // waiting forever for a synthetic NexusMod_* filename.
            if (expectedBase.StartsWith("NexusMod_", StringComparison.OrdinalIgnoreCase))
            {
                return candidates
                    .Where(p =>
                    {
                        var ext = Path.GetExtension(p);
                        return ext.Equals(".zip", StringComparison.OrdinalIgnoreCase) ||
                               ext.Equals(".7z", StringComparison.OrdinalIgnoreCase) ||
                               ext.Equals(".rar", StringComparison.OrdinalIgnoreCase) ||
                               ext.Equals(".rar5", StringComparison.OrdinalIgnoreCase);
                    })
                    .OrderByDescending(p => File.GetLastWriteTimeUtc(p))
                    .FirstOrDefault();
            }
        }
        catch { }
        return null;
    }

    private async Task CopyFileWithProgressAsync(string source, string destination, ActiveDownloadState state)
    {
        var total = new FileInfo(source).Length;
        long copied = 0;
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[128 * 1024];
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read));
            copied += read;
            UpdateActiveDownload(state, copied, total);
        }
        await output.FlushAsync();
    }

    private static string JsonString(JsonElement item, params string[] names)
    {
        if (item.ValueKind != JsonValueKind.Object) return "";
        foreach (var name in names)
        {
            if (!item.TryGetProperty(name, out var value)) continue;
            if (value.ValueKind == JsonValueKind.String) return value.GetString() ?? "";
            if (value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                return value.ToString();
            if (value.ValueKind == JsonValueKind.Object)
            {
                foreach (var nested in new[] { "name", "username", "value" })
                    if (value.TryGetProperty(nested, out var n) && n.ValueKind == JsonValueKind.String)
                        return n.GetString() ?? "";
            }
        }
        return "";
    }

    private void OpenModFolder(ModEntry mod)
    {
        try
        {
            if (Directory.Exists(mod.Path)) OpenFolder(mod.Path);
            else if (File.Exists(mod.Path)) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{mod.Path}\"") { UseShellExecute = true });
        }
        catch { }
    }

    private static void OpenFolder(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }); } catch { }
    }

    private static void OpenFile(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
    }

    private static void RevealInExplorer(string path)
    {
        try
        {
            if (File.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            else if (Directory.Exists(path)) OpenFolder(path);
        }
        catch { }
    }

    private void ChangeModName(ModEntry mod)
    {
        try
        {
            var gameRoot = GetVerifiedGameRoot();
            var data = LoadNexusMetadata();
            var key = mod.IsPak ? PakMetadataKey(mod.Path) : MetadataKey(gameRoot, mod.Path);
            data.TryGetValue(key, out var existing);

            var nexusName = existing?.Name;
            var fileName = mod.IsPak
                ? Path.GetFileNameWithoutExtension(Path.GetFileName(mod.Path))
                : Path.GetFileName(mod.Path);
            var currentName = !string.IsNullOrWhiteSpace(existing?.DisplayName)
                ? existing!.DisplayName
                : (!string.IsNullOrWhiteSpace(existing?.Name) ? existing.Name : fileName);

            var dialog = new Window
            {
                Owner = this,
                Width = 600,
                Height = 340,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Title = L("Change Name"),
                ResizeMode = ResizeMode.NoResize,
                Background = (Brush)Resources["WindowBackgroundBrush"],
                Foreground = (Brush)Resources["ForegroundBrush"]
            };

            var panel = new StackPanel { Margin = new Thickness(20) };
            panel.Children.Add(new TextBlock
            {
                Text = L("Nexus Mod Name"),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            });
            panel.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(nexusName) ? L("Not linked to Nexus") : nexusName,
                Foreground = (Brush)Resources["SecondaryBrush"],
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            });
            panel.Children.Add(new TextBlock
            {
                Text = mod.IsPak ? L("PAK File Name") : L("Mod Folder Name"),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            });
            panel.Children.Add(new TextBlock
            {
                Text = fileName,
                Foreground = (Brush)Resources["SecondaryBrush"],
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            });
            panel.Children.Add(new TextBlock
            {
                Text = L("Display Name"),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            });
            var box = new TextBox
            {
                Text = currentName,
                Style = (Style)Resources["InputStyle"]
            };
            panel.Children.Add(box);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };
            var cancel = new Button
            { Content = L("Cancel"), Style = (Style)Resources["BrowseButtonStyle"], MinWidth = 90 };
            var save = new Button
            { Content = L("Save"), Style = (Style)Resources["AccentButtonStyle"], MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
            cancel.Click += (_, _) => { dialog.DialogResult = false; dialog.Close(); };
            save.Click += (_, _) =>
            {
                var displayName = box.Text.Trim();
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    MessageBox.Show(L("Please enter a name."), L("Change Name"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (existing == null)
                {
                    existing = new NexusModMetadata(displayName, "", 0, 0, "") { DisplayName = displayName };
                }
                else
                {
                    existing = existing with { DisplayName = displayName }; // Individual file name only; GroupDisplayName is intentionally preserved.
                }
                data[key] = existing;
                SaveNexusMetadata(data);
                dialog.DialogResult = true;
                dialog.Close();
            };
            buttons.Children.Add(cancel);
            buttons.Children.Add(save);
            panel.Children.Add(buttons);

            dialog.Content = panel;
            dialog.KeyDown += (_, args) =>
            {
                if (args.Key == Key.Escape)
                {
                    args.Handled = true;
                    dialog.DialogResult = false;
                    dialog.Close();
                }
                else if (args.Key == Key.Enter && Keyboard.FocusedElement == box)
                {
                    save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
            };

            if (dialog.ShowDialog() == true) RefreshModManager();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, L("Mod Manager"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void MovePakToBoundary(ModEntry mod, bool toTop)
    {
        if (!mod.IsPak || string.IsNullOrWhiteSpace(mod.Path)) return;
        try
        {
            var order = GetOrderedPakPaths();
            var root = GetVerifiedGameRoot();
            // Preserve the enabled/disabled state against the old positional names.
            var enabledSources = GetEnabledPakSources(GetPakModsRoot(root));
            var path = Path.GetFullPath(mod.Path);
            var groupPaths = GetPakGroupPathsForPath(path);
            var moving = groupPaths.Count > 1 ? groupPaths : new List<string> { path };
            var movingSet = moving.ToHashSet(StringComparer.OrdinalIgnoreCase);
            order.RemoveAll(p => movingSet.Contains(p));
            if (toTop) order.InsertRange(0, moving);
            else order.AddRange(moving);

            SavePakLoadOrder(order);
            RebuildPakLinks(root, enabledSources);
            RefreshModManagerAfterStateChange();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, L("Mod Manager"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ModContextButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not ModEntry mod) return;
        var menu = new ContextMenu();
        menu.Items.Add(MenuItem(mod.Enabled ? L("Disable") : L("Enable"), (_, _) => ModToggle_Click(button, new RoutedEventArgs())));
        if (!mod.IsUe4ssDefault)
            menu.Items.Add(MenuItem(L("Delete"), (_, _) => ModDelete_Click(button, new RoutedEventArgs())));
        menu.Items.Add(MenuItem(L("Change Name"), (_, _) => ChangeModName(mod)));
        if (mod.IsPak)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItem(L("Move to Top"), (_, _) => MovePakToBoundary(mod, true)));
            menu.Items.Add(MenuItem(L("Move to Bottom"), (_, _) => MovePakToBoundary(mod, false)));
        }
        if (mod.IsPak && GetOtherPakVersions(mod).Count > 0)
            menu.Items.Add(MenuItem(L("Other Versions"), (_, _) => ShowOtherPakVersionsDialog(mod)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem(L("Open Mod Folder"), (_, _) => OpenModFolder(mod)));
        var nexusKey = mod.IsPak ? PakMetadataKey(mod.Path) : MetadataKey(GetVerifiedGameRoot(), mod.Path);
        var nexus = LoadNexusMetadata().GetValueOrDefault(nexusKey);
        if (nexus != null && nexus.ModId > 0)
        {
            menu.Items.Add(MenuItem(L("Open Nexus Page"), (_, _) => OpenUrl($"https://www.nexusmods.com/{nexus.Game}/mods/{nexus.ModId}")));
            menu.Items.Add(MenuItem(L("Unlink Nexus"), (_, _) => UnlinkNexus(mod)));
        }
        else
        {
            menu.Items.Add(MenuItem(L("Link to Nexus"), (_, _) => LinkModToNexus(mod)));
        }
        menu.IsOpen = true;
    }

    private static MenuItem MenuItem(string text, RoutedEventHandler handler)
    {
        var item = new MenuItem { Header = text };
        item.Click += handler;
        return item;
    }

    private void ShowOtherPakVersionsDialog(ModEntry mod)
    {
        var versions = GetOtherPakVersions(mod);
        var dialog = new Window
        {
            Owner = this, Width = 760, Height = 430, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Title = L("Other Versions"), Background = (Brush)Resources["WindowBackgroundBrush"], Foreground = (Brush)Resources["ForegroundBrush"]
        };
        var root = new DockPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock { Text = L("Other Versions"), FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        var list = new StackPanel();
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list };
        DockPanel.SetDock(scroll, Dock.Bottom);
        root.Children.Add(scroll);
        foreach (var version in versions)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            grid.Children.Add(new TextBlock { Text = version.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            var versionText = new TextBlock { Text = version.Version, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)Resources["SecondaryBrush"] };
            Grid.SetColumn(versionText, 1); grid.Children.Add(versionText);
            var dateText = new TextBlock { Text = version.Date, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)Resources["SecondaryBrush"] };
            Grid.SetColumn(dateText, 2); grid.Children.Add(dateText);

            var installImage = new Border { Width = 18, Height = 18, Background = (Brush)Resources["ForegroundBrush"], OpacityMask = new ImageBrush(LoadModIcon("Install.png")) { Stretch = Stretch.Uniform } };
            var install = new Button { Content = installImage, Width = 34, Height = 34, Style = (Style)Resources["ModIconButtonStyle"], ToolTip = L("Install") };
            install.Click += (_, _) =>
            {
                try { InstallPakVersion(mod, version); dialog.Close(); }
                catch (Exception ex) { MessageBox.Show(ex.Message, L("Other Versions"), MessageBoxButton.OK, MessageBoxImage.Error); }
            };
            Grid.SetColumn(install, 3); grid.Children.Add(install);

            var deleteImage = new Border { Width = 18, Height = 18, Background = (Brush)Resources["ForegroundBrush"], OpacityMask = new ImageBrush(LoadModIcon("delete.png")) { Stretch = Stretch.Uniform } };
            var delete = new Button { Content = deleteImage, Width = 34, Height = 34, Style = (Style)Resources["ModIconButtonStyle"], ToolTip = L("Delete") };
            delete.Click += (_, _) =>
            {
                if (MessageBox.Show(L("Delete version '{0}'?\n\nThis cannot be undone.", version.Version), L("Delete Version"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                try { File.Delete(version.PakPath); File.Delete(version.JsonPath); dialog.Close(); ShowOtherPakVersionsDialog(mod); }
                catch (Exception ex) { MessageBox.Show(ex.Message, L("Delete Version"), MessageBoxButton.OK, MessageBoxImage.Error); }
            };
            Grid.SetColumn(delete, 4); grid.Children.Add(delete);
            list.Children.Add(grid);
        }
        if (versions.Count == 0) list.Children.Add(new TextBlock { Text = L("No other versions are stored."), Foreground = (Brush)Resources["SecondaryBrush"] });
        var close = new Button { Content = L("Close"), Style = (Style)Resources["BrowseButtonStyle"], MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        close.Click += (_, _) => dialog.Close();
        DockPanel.SetDock(close, Dock.Bottom); root.Children.Add(close);
        dialog.Content = root;
        dialog.ShowDialog();
    }

    private void InstallPakVersion(ModEntry mod, PakVersionInfo version)
    {
        var folder = GetPakModFolderForPath(mod.Path);
        var versionsFolder = Path.Combine(folder, "_versions");
        Directory.CreateDirectory(versionsFolder);
        var current = GetActivePakInModFolder(folder);
        var currentJson = current == null ? "" : GetJsonForPak(current);
        var preserveEnabled = false;
        var gameRoot = GetVerifiedGameRoot();
        if (current != null)
        {
            var currentName = Path.GetFileName(current);
            var currentEnabledTarget = Path.Combine(GetPakModsRoot(gameRoot), currentName);
            preserveEnabled = File.Exists(currentEnabledTarget) || IsSymbolicLink(currentEnabledTarget);
            try { if (File.Exists(currentEnabledTarget) || IsSymbolicLink(currentEnabledTarget)) File.Delete(currentEnabledTarget); } catch { }
            try { if (File.Exists(currentEnabledTarget + ".RRModHub.DISABLED")) File.Delete(currentEnabledTarget + ".RRModHub.DISABLED"); } catch { }

            var destPak = Path.Combine(versionsFolder, Path.GetFileName(current));
            var destJson = Path.Combine(versionsFolder, Path.GetFileName(currentJson));
            CopyOrMoveFile(current, destPak);
            if (File.Exists(currentJson)) CopyOrMoveFile(currentJson, destJson);
            var oldData = LoadNexusMetadata();
            oldData.Remove(PakMetadataKey(current));
            SaveNexusMetadata(oldData);
            InvalidatePakConflictIndexForPath(current);
        }

        var newActive = Path.Combine(folder, Path.GetFileName(version.PakPath));
        var newJson = Path.Combine(folder, Path.GetFileName(version.JsonPath));
        CopyOrMoveFile(version.PakPath, newActive);
        if (File.Exists(version.JsonPath)) CopyOrMoveFile(version.JsonPath, newJson);
        if (TryLoadPakVersionManifest(newJson, out var manifest))
        {
            var data = LoadNexusMetadata();
            data[PakMetadataKey(newActive)] = new NexusModMetadata(
                manifest.NexusName.Length > 0 ? manifest.NexusName : manifest.ModName,
                manifest.NexusGame ?? "", manifest.NexusModId, manifest.NexusFileId, manifest.OriginalPakName)
            {
                DisplayName = manifest.DisplayName,
                InstalledVersion = manifest.Version,
                LatestVersion = manifest.LatestVersion
            };
            SaveNexusMetadata(data);
        }
        SetPakPathEnabled(gameRoot, newActive, preserveEnabled);
        _cachedPakMods = null;
        _modCacheUpdatedUtc = DateTime.MinValue;
        RefreshModManager();
        if (!_gameActive && _mode == "conflicts") RefreshConflictCheckPage();
    }

    private PakInstallChoice ShowNexusMultiFileInstallChoice(string modName, string fileName, int fileCount)
    {
        var dialog = new Window
        {
            Owner = this,
            Width = 520,
            Height = 300,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Title = L("Multiple Files Available"),
            Background = (Brush)Resources["WindowBackgroundBrush"],
            Foreground = (Brush)Resources["ForegroundBrush"]
        };
        var root = new StackPanel { Margin = new Thickness(22) };
        root.Children.Add(new TextBlock { Text = L("Multiple files are available for this mod."), FontSize = 20, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        root.Children.Add(new TextBlock { Text = L("{0}\nSelected file: {1}\n{2} current files found (Old and Archived files are ignored).", modName, fileName, fileCount), Margin = new Thickness(0, 12, 0, 18), Foreground = (Brush)Resources["SecondaryBrush"], TextWrapping = TextWrapping.Wrap });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var update = new Button { Content = L("Update Existing Mod"), Style = (Style)Resources["BrowseButtonStyle"], Margin = new Thickness(0, 0, 8, 0), MinWidth = 150 };
        var add = new Button { Content = L("Add as New"), Style = (Style)Resources["BrowseButtonStyle"], Margin = new Thickness(0, 0, 8, 0), MinWidth = 120 };
        var cancel = new Button { Content = L("Cancel"), Style = (Style)Resources["BrowseButtonStyle"], MinWidth = 90 };
        var result = PakInstallChoice.Cancel;
        update.Click += (_, _) => { result = PakInstallChoice.UpdateExisting; dialog.DialogResult = true; };
        add.Click += (_, _) => { result = PakInstallChoice.AddAsNew; dialog.DialogResult = true; };
        cancel.Click += (_, _) => { result = PakInstallChoice.Cancel; dialog.DialogResult = false; };
        buttons.Children.Add(update); buttons.Children.Add(add); buttons.Children.Add(cancel); root.Children.Add(buttons);
        dialog.Content = root;
        dialog.ShowDialog();
        return result;
    }

    private void PendingInstall_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not PendingModEntry pending) return;
        _ = InstallPendingModAsync(pending);
    }

    private async Task InstallPendingModAsync(PendingModEntry pending)
    {
        try
        {
            var importedMeta = ImportMo2MetaForDownload(pending.ZipPath);
            await InstallModZipAsync(pending.ZipPath, importedMeta?.Name ?? pending.Name, importedMeta?.Game ?? pending.NexusGame, importedMeta?.ModId ?? pending.NexusModId, importedMeta?.FileId ?? pending.NexusFileId);
            RefreshModManager();
            RefreshDownloadsPage();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, L("Install Mod"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PendingContextButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not PendingModEntry pending) return;
        var menu = new ContextMenu();
        menu.Items.Add(MenuItem(L("Install"), (_, _) => _ = InstallPendingModAsync(pending)));
        menu.Items.Add(MenuItem(L("Delete Download"), (_, _) => DeletePendingDownload(pending)));
        menu.Items.Add(MenuItem(L("Open Download Folder"), (_, _) => OpenFolder(GetDownloadsDirectory())));
        menu.Items.Add(new Separator());
        var pendingMetadata = LoadNexusMetadata().GetValueOrDefault("_download:" + Path.GetFileName(pending.ZipPath));
        if (pendingMetadata != null && pendingMetadata.ModId > 0)
            menu.Items.Add(MenuItem(L("Open Nexus Page"), (_, _) => OpenUrl($"https://www.nexusmods.com/{pendingMetadata.Game}/mods/{pendingMetadata.ModId}")));
        else
            menu.Items.Add(MenuItem(L("Link Download to Nexus"), (_, _) => LinkPendingToNexus(pending)));
        menu.IsOpen = true;
    }

    private void DeletePendingDownload(PendingModEntry pending)
    {
        if (MessageBox.Show(L("Delete downloaded mod '{0}'?", pending.Name), L("Delete Download"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { File.Delete(pending.ZipPath); RefreshModManager(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, L("Mod Manager"), MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void LinkPendingToNexus(PendingModEntry pending)
    {
        var input = ShowTextInputDialog(L("Enter the Retro Rewind Nexus Mods page URL:"), L("Link to Nexus"), "https://www.nexusmods.com/retrorewindvideostoresimulator/mods/");
        if (string.IsNullOrWhiteSpace(input)) return;
        if (!Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri) || !uri.Host.Equals("www.nexusmods.com", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(L("Please enter a valid Retro Rewind Nexus URL."), L("Link to Nexus"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || !parts[^2].Equals("mods", StringComparison.OrdinalIgnoreCase) || !int.TryParse(parts[^1], out var modId))
        {
            MessageBox.Show(L("Please enter a valid Retro Rewind Nexus mod page URL."), L("Link to Nexus"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var key = "_download:" + Path.GetFileName(pending.ZipPath);
        var data = LoadNexusMetadata();
        var existingPending = data.GetValueOrDefault(key);
        data[key] = new NexusModMetadata(pending.Name, parts[0], modId, 0, Path.GetFileName(pending.ZipPath))
        { DownloadedAtUtc = existingPending?.DownloadedAtUtc ?? File.GetCreationTimeUtc(pending.ZipPath) };
        SaveNexusMetadata(data);
        RefreshModManager();
    }

    private void TogglePakMultiSelection(string path, FrameworkElement row)
    {
        if (_selectedPakModPaths.Contains(path))
            _selectedPakModPaths.Remove(path);
        else
            _selectedPakModPaths.Add(path);
        _lastPakSelectionUtc = DateTime.UtcNow;

        if (row is Grid grid)
        {
            grid.Background = _selectedPakModPaths.Contains(path)
                ? new SolidColorBrush(((SolidColorBrush)Resources["AccentBrush"]).Color) { Opacity = 0.22 }
                : Brushes.Transparent;
        }
        UpdatePakBatchLinkButton();
    }

    private void UpdatePakBatchLinkButton()
    {
        if (PakBatchLinkButton == null) return;
        var existing = GetPakMods(GetVerifiedGameRoot()).Select(m => m.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _selectedPakModPaths.RemoveWhere(p => !existing.Contains(p));
        PakBatchLinkButton.Visibility = _selectedPakModPaths.Count >= 2 ? Visibility.Visible : Visibility.Collapsed;
        PakBatchLinkButton.Content = L("Batch Link ({0})", _selectedPakModPaths.Count);
    }

    private async void PakBatchLinkButton_Click(object sender, RoutedEventArgs e)
    {
        var paths = _selectedPakModPaths.ToList();
        if (paths.Count < 2) return;
        var input = ShowTextInputDialog(L("Enter the Retro Rewind Nexus Mods page URL:"), L("Batch Link"), "https://www.nexusmods.com/retrorewindvideostoresimulator/mods/");
        if (string.IsNullOrWhiteSpace(input)) return;
        if (!Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri) || !uri.Host.Equals("www.nexusmods.com", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, L("Please enter a valid Retro Rewind Nexus URL."), L("Batch Link"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || !parts[^2].Equals("mods", StringComparison.OrdinalIgnoreCase) || !int.TryParse(parts[^1], out var modId))
        {
            MessageBox.Show(this, L("Please enter a valid Retro Rewind Nexus mod page URL."), L("Batch Link"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var game = parts[0];
        NexusModInfo? info = null;
        try { info = await FetchNexusModBasicInfoAsync(game, modId); } catch { }
        var data = LoadNexusMetadata();
        foreach (var path in paths)
        {
            var key = PakMetadataKey(path);
            var existing = data.GetValueOrDefault(key);
            var nexusName = info?.Name;
            if (string.IsNullOrWhiteSpace(nexusName)) nexusName = existing?.Name;
            if (string.IsNullOrWhiteSpace(nexusName)) nexusName = Path.GetFileNameWithoutExtension(path);
            var meta = existing ?? new NexusModMetadata(nexusName!, game, modId, 0, "");
            data[key] = meta with
            {
                Name = nexusName!,
                Game = game,
                ModId = modId,
                LatestVersion = info?.Version ?? meta.LatestVersion,
                Description = info?.Description ?? meta.Description
            };
        }
        SaveNexusMetadata(data);
        _selectedPakModPaths.Clear();
        RefreshModManager();
    }

    private async Task<NexusModInfo?> FetchNexusModBasicInfoAsync(string game, int modId)
    {
        var apiKey = string.IsNullOrWhiteSpace(_nexusApiKey) ? NexusSecretStore.Load() : _nexusApiKey;
        if (string.IsNullOrWhiteSpace(apiKey)) return null;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RetroRewindModHub/1.0.0");
        client.DefaultRequestHeaders.TryAddWithoutValidation("apikey", apiKey);
        using var response = await client.GetAsync($"https://api.nexusmods.com/v1/games/{Uri.EscapeDataString(game)}/mods/{modId}");
        if (!response.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        var info = new NexusModInfo
        {
            Name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
            Version = root.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "",
            Description = root.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
            Author = root.TryGetProperty("author", out var a) ? a.GetString() ?? "" : ""
        };
        SaveNexusDescriptionCache(game, modId, info.Name, info.Version, info.Description);
        return info;
    }

    private void UnlinkNexus(ModEntry mod)
    {
        var root = GetVerifiedGameRoot();
        var data = LoadNexusMetadata();
        var key = mod.IsPak ? PakMetadataKey(mod.Path) : MetadataKey(root, mod.Path);
        if (data.Remove(key)) SaveNexusMetadata(data);
        RefreshModManager();
    }

    private async void LinkModToNexus(ModEntry mod)
    {
        var input = ShowTextInputDialog(L("Enter the Retro Rewind Nexus Mods page URL:"), L("Link to Nexus"), "https://www.nexusmods.com/retrorewindvideostoresimulator/mods/");
        if (string.IsNullOrWhiteSpace(input)) return;
        if (!Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri) || !uri.Host.Equals("www.nexusmods.com", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(L("Please enter a valid Retro Rewind Nexus Mods URL."), L("Link to Nexus"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || !parts[^2].Equals("mods", StringComparison.OrdinalIgnoreCase) || !int.TryParse(parts[^1], out var modId))
        {
            MessageBox.Show(L("Please enter a valid Retro Rewind Nexus mod page URL."), L("Link to Nexus"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var game = parts[0];
        var existingModMeta = mod.IsPak
            ? LoadNexusMetadata().GetValueOrDefault(PakMetadataKey(mod.Path))
            : LoadNexusMetadata().GetValueOrDefault(MetadataKey(GetVerifiedGameRoot(), mod.Path));
        var meta = new NexusModMetadata(mod.Name, game, modId, 0, existingModMeta?.ArchivePath ?? "")
        {
            DisplayName = existingModMeta?.DisplayName ?? ""
        };
        try
        {
            var info = await FetchNexusModInfoAsync(game, modId);
            if (info != null) meta = ApplyNexusInfo(meta, info);
        }
        catch { }

        if (mod.IsPak)
        {
            try
            {
                var data = LoadNexusMetadata();
                var current = data.GetValueOrDefault(PakMetadataKey(mod.Path));
                var version = current?.InstalledVersion;
                if (string.IsNullOrWhiteSpace(version) && TryLoadPakVersionManifest(GetJsonForPak(mod.Path), out var currentManifest))
                    version = currentManifest.Version;
                if (string.IsNullOrWhiteSpace(version)) version = "Unknown";
                var fileCount = -1;
                try { fileCount = await FetchNexusCurrentFileCountAsync(game, modId); } catch { }
                meta = meta with
                {
                    ArchivePath = existingModMeta?.ArchivePath ?? meta.ArchivePath,
                    InstalledVersion = version!,
                    LatestVersion = string.IsNullOrWhiteSpace(meta.LatestVersion) ? version! : meta.LatestVersion,
                    NexusCurrentFileCount = fileCount,
                    DisplayName = existingModMeta?.DisplayName ?? meta.DisplayName
                };
                var renamedPath = RelinkPakToNexus(mod.Path, meta, version!);
                data.Remove(PakMetadataKey(mod.Path));
                data[PakMetadataKey(renamedPath)] = meta;
                SaveNexusMetadata(data);
                EnsurePakJsonManifests();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, L("The mod was linked, but its local PAK naming could not be updated:\n\n{0}", ex.Message), L("Link to Nexus"), MessageBoxButton.OK, MessageBoxImage.Warning);
                var data = LoadNexusMetadata();
                data[PakMetadataKey(mod.Path)] = meta;
                SaveNexusMetadata(data);
            }
        }
        else
        {
            var data = LoadNexusMetadata();
            data[MetadataKey(GetVerifiedGameRoot(), mod.Path)] = meta;
            SaveNexusMetadata(data);
        }
        RefreshModManager();
    }

    private string RelinkPakToNexus(string pakPath, NexusModMetadata meta, string version)
    {
        var root = GetPakVirtualRoot();
        Directory.CreateDirectory(root);
        var originalPak = pakPath;
        var originalFamily = GetPakModFamilyFolderForPath(originalPak);
        var originalRelative = string.Equals(originalFamily, root, StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileName(originalPak)
            : Path.GetRelativePath(originalFamily, originalPak);
        var desiredFamily = Path.Combine(root, SanitizePakFolderName(meta.Name));

        if (string.Equals(originalFamily, root, StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(desiredFamily);
        }
        else if (!string.Equals(originalFamily, desiredFamily, StringComparison.OrdinalIgnoreCase))
        {
            if (Directory.Exists(desiredFamily))
            {
                var packageName = Path.GetFileName(originalFamily);
                desiredFamily = Path.Combine(root, SanitizePakFolderName(meta.Name) + "_" + SanitizePakFolderName(packageName));
            }
            Directory.Move(originalFamily, desiredFamily);
        }

        var currentPak = string.Equals(originalFamily, root, StringComparison.OrdinalIgnoreCase)
            ? originalPak
            : Path.Combine(desiredFamily, originalRelative);
        if (!File.Exists(currentPak)) currentPak = originalPak;
        var packageFolder = Path.GetDirectoryName(currentPak) ?? desiredFamily;
        Directory.CreateDirectory(packageFolder);

        var gameRoot = GetVerifiedGameRoot();
        var oldEnabledName = Path.GetFileName(originalPak);
        var oldEnabledTarget = Path.Combine(GetPakModsRoot(gameRoot), oldEnabledName);
        var oldDisabledTarget = oldEnabledTarget + ".RRModHub.DISABLED";
        var enabled = File.Exists(oldEnabledTarget) || IsSymbolicLink(oldEnabledTarget);
        if (File.Exists(oldEnabledTarget) || IsSymbolicLink(oldEnabledTarget)) File.Delete(oldEnabledTarget);
        if (File.Exists(oldDisabledTarget)) File.Delete(oldDisabledTarget);

        if (string.Equals(originalFamily, root, StringComparison.OrdinalIgnoreCase))
        {
            var movedIntoFamily = Path.Combine(desiredFamily, Path.GetFileName(currentPak));
            var movedJson = GetJsonForPak(currentPak);
            var movedJsonTarget = GetJsonForPak(movedIntoFamily);
            File.Move(currentPak, movedIntoFamily);
            if (File.Exists(movedJson)) File.Move(movedJson, movedJsonTarget);
            currentPak = movedIntoFamily;
        }

        var timestamp = GetPakVersionTimestamp(DateTime.UtcNow);
        var targetName = $"{timestamp}_{SanitizePakVersionPart(version)}.pak";
        var target = Path.Combine(packageFolder, targetName);
        var n = 2;
        while (File.Exists(target) && !string.Equals(target, currentPak, StringComparison.OrdinalIgnoreCase))
            target = Path.Combine(packageFolder, $"{timestamp}_{SanitizePakVersionPart(version)}_{n++}.pak");

        var json = GetJsonForPak(currentPak);
        var targetJson = GetJsonForPak(target);
        if (!string.Equals(currentPak, target, StringComparison.OrdinalIgnoreCase))
        {
            File.Move(currentPak, target);
            if (File.Exists(json)) File.Move(json, targetJson);
        }
        var manifest = BuildPakVersionManifest(target, meta.Name, version, meta);
        SavePakVersionManifest(targetJson, manifest);
        SetPakPathEnabled(gameRoot, target, enabled);
        InvalidatePakConflictIndexForPath(originalFamily);
        return target;
    }

    private string? ShowTextInputDialog(string message, string title, string initialValue)
    {
        var dialog = new Window
        {
            Owner = this, Width = 560, Height = 230, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Title = title, ResizeMode = ResizeMode.NoResize, Background = (Brush)Resources["WindowBackgroundBrush"]
        };
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Resources["ForegroundBrush"], Margin = new Thickness(0,0,0,8) });
        var box = new TextBox { Text = initialValue, Style = (Style)Resources["InputStyle"] };
        panel.Children.Add(box);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,14,0,0) };
        var ok = new Button { Content = L("OK"), Style = (Style)Resources["AccentButtonStyle"], MinWidth = 90, Margin = new Thickness(6,0,0,0) };
        var cancel = new Button { Content = L("Cancel"), Style = (Style)Resources["BrowseButtonStyle"], MinWidth = 90 };
        ok.Click += (_, _) => { dialog.DialogResult = true; dialog.Close(); };
        cancel.Click += (_, _) => { dialog.DialogResult = false; dialog.Close(); };
        buttons.Children.Add(cancel); buttons.Children.Add(ok); panel.Children.Add(buttons);
        dialog.Content = panel;
        return dialog.ShowDialog() == true ? box.Text : null;
    }

    private void MoveNexusMetadata(string gameRoot, string oldPath, string newPath)
    {
        var data = LoadNexusMetadata();
        var oldKey = MetadataKey(gameRoot, oldPath);
        if (!data.TryGetValue(oldKey, out var meta)) return;
        data.Remove(oldKey);
        data[MetadataKey(gameRoot, newPath)] = meta;
        SaveNexusMetadata(data);
    }

    private void RefreshModManagerAfterStateChange()
    {
        // State-changing operations must repaint the CURRENT page immediately.
        // The normal Mod Manager refresh intentionally has a short cache window,
        // which is useful for navigation but wrong after an enable/disable click:
        // it could simply paint the old ModEntry objects back into the list.
        _modCacheUpdatedUtc = DateTime.MinValue;
        try { _modRefreshCts?.Cancel(); } catch { }
        _modRefreshInProgress = false;

        if (_mode != "mods" || !IsLoaded) return;

        var refreshVersion = Interlocked.Increment(ref _modStateRefreshVersion);
        var pakScrollOffset = GetScrollViewerVerticalOffset(PakModsList);
        var ueScrollOffset = GetScrollViewerVerticalOffset(Ue4ssModsList);
        _modRefreshInProgress = true;

        var cts = new CancellationTokenSource();
        _modRefreshCts = cts;
        _ = Task.Run(() =>
        {
            cts.Token.ThrowIfCancellationRequested();
            var gameRoot = GetVerifiedGameRoot();
            var pak = GetPakMods(gameRoot);
            cts.Token.ThrowIfCancellationRequested();
            var ue = GetUe4ssMods(gameRoot);
            cts.Token.ThrowIfCancellationRequested();
            var pending = GetPendingMods();
            return (pak, ue, pending);
        }, cts.Token).ContinueWith(t =>
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (refreshVersion != _modStateRefreshVersion) return;
                _modRefreshInProgress = false;
                if (t.IsCanceled || t.IsFaulted || cts.IsCancellationRequested || _gameActive || _mode != "mods") return;

                _cachedPakMods = t.Result.pak;
                _cachedUe4ssMods = t.Result.ue;
                _cachedPendingMods = t.Result.pending;
                _modCacheUpdatedUtc = DateTime.UtcNow;
                SaveModListCache();
                ApplyModManagerSnapshot(_cachedPakMods, _cachedUe4ssMods, _cachedPendingMods);

                // Rebuilding the ListBox recreates its containers, so restore the
                // user's position on the page after layout has completed.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    SetScrollViewerVerticalOffset(PakModsList, pakScrollOffset);
                    SetScrollViewerVerticalOffset(Ue4ssModsList, ueScrollOffset);
                }), DispatcherPriority.Loaded);
            }));
        }, TaskScheduler.Default);
    }

    private static double GetScrollViewerVerticalOffset(DependencyObject root)
    {
        if (root == null) return 0;
        var viewer = FindDescendant<ScrollViewer>(root);
        return viewer?.VerticalOffset ?? 0;
    }

    private static void SetScrollViewerVerticalOffset(DependencyObject root, double offset)
    {
        if (root == null) return;
        var viewer = FindDescendant<ScrollViewer>(root);
        viewer?.ScrollToVerticalOffset(offset);
    }

    private async void ModToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not ModEntry mod || _operationBusy) return;
        try
        {
            var newEnabled = !mod.Enabled;
            if (mod.IsUe4ssDefault && !newEnabled)
            {
                var result = MessageBox.Show(this,
                    L("Warning — UE4SS Default Mod\n\n'{0}' is a protected UE4SS default mod. Disabling it may affect UE4SS or game functionality.\n\nDo you want to disable it?", mod.Name),
                    L("Disable UE4SS Default Mod"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes) return;
            }

            SetOperationBusy(true, newEnabled ? L("Enabling mod…") : L("Disabling mod…"), null, mod.Name);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            await Task.Yield();

            if (mod.IsPak)
                SetPakModEnabled(mod, newEnabled);
            else
                SetUe4ssModEnabled(mod, newEnabled);

            // Force a filesystem-backed refresh so the row icon, "(disabled)"
            // label, tooltip and context-menu state all reflect the operation.
            RefreshModManagerAfterStateChange();
            SetOperationBusy(false);
        }
        catch (Exception ex)
        {
            SetOperationBusy(false);
            MessageBox.Show(ex.Message, L("Mod Manager"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetPakModEnabled(ModEntry mod, bool enabled)
    {
        SetPakModEnabledWithoutRefresh(mod, enabled);
    }

    private void SetUe4ssModEnabled(ModEntry mod, bool enabled)
    {
        var gameRoot = GetVerifiedGameRoot();
        var modsRoot = GetUe4ssModsRoot(gameRoot);

        // ModEntry.Name may be the user's display name from metadata. UE4SS
        // mods.txt must use the actual UE4SS mod folder name, so always derive
        // the key from the installed directory.
        var folderName = Path.GetFileName(
            mod.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        if (string.IsNullOrWhiteSpace(folderName))
            throw new InvalidOperationException(L("The UE4SS mod folder could not be determined."));

        SetUe4ssModsTxtEnabled(modsRoot, folderName, enabled);
    }

    private static string GetUe4ssModsTxtPath(string modsRoot) =>
        Path.Combine(modsRoot, "mods.txt");

    private static bool TryParseUe4ssModsTxtLine(string line, out string modName, out bool enabled)
    {
        modName = "";
        enabled = false;
        if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith(";", StringComparison.Ordinal))
            return false;

        var split = line.IndexOf(':');
        if (split <= 0) return false;

        modName = line[..split].Trim();
        var state = line[(split + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(modName)) return false;
        if (state == "1") enabled = true;
        else if (state == "0") enabled = false;
        else return false;
        return true;
    }

    private static bool ReadUe4ssModsTxtEnabled(string modsRoot, string modName, bool defaultValue)
    {
        var path = GetUe4ssModsTxtPath(modsRoot);
        if (!File.Exists(path)) return defaultValue;

        try
        {
            foreach (var line in File.ReadAllLines(path))
            {
                if (TryParseUe4ssModsTxtLine(line, out var name, out var enabled) &&
                    string.Equals(name, modName, StringComparison.OrdinalIgnoreCase))
                    return enabled;
            }
        }
        catch (IOException) { }

        return defaultValue;
    }

    private static void SetUe4ssModsTxtEnabled(string modsRoot, string modName, bool enabled)
    {
        EnsureUe4ssModsTxtFile(modsRoot);
        var path = GetUe4ssModsTxtPath(modsRoot);
        var lines = File.ReadAllLines(path).ToList();

        var firstMatch = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            if (!TryParseUe4ssModsTxtLine(lines[i], out var name, out _) ||
                !string.Equals(name, modName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (firstMatch < 0)
            {
                firstMatch = i;
                lines[i] = $"{name} : {(enabled ? "1" : "0")}";
            }
            else if (!Ue4ssDefaultModNames.Contains(name))
            {
                // Remove duplicate user entries so UE4SS has one authoritative
                // state for this mod. Protected baseline entries are untouched.
                lines.RemoveAt(i--);
            }
        }

        if (firstMatch < 0)
            InsertUe4ssModTxtEntry(lines, modName, enabled);

        File.WriteAllLines(path, lines);
    }

    private static void EnsureUe4ssModsTxtFile(string modsRoot)
    {
        Directory.CreateDirectory(modsRoot);
        var path = GetUe4ssModsTxtPath(modsRoot);

        if (!File.Exists(path))
        {
            File.WriteAllLines(path, Ue4ssProtectedModsTxtLines);
            return;
        }

        var lines = File.ReadAllLines(path).ToList();

        // Never rebuild an existing file. Add only missing protected baseline
        // entries, retaining every existing line and its current state.
        foreach (var baseline in Ue4ssProtectedModsTxtLines)
        {
            if (!TryParseUe4ssModsTxtLine(baseline, out var baselineName, out _))
                continue;

            if (lines.Any(line =>
                TryParseUe4ssModsTxtLine(line, out var name, out _) &&
                string.Equals(name, baselineName, StringComparison.OrdinalIgnoreCase)))
                continue;

            var keybindIndex = lines.FindIndex(line =>
                line.TrimStart().StartsWith("; Built-in keybinds", StringComparison.OrdinalIgnoreCase));

            if (baselineName.Equals("Keybinds", StringComparison.OrdinalIgnoreCase))
                lines.Add(baseline);
            else if (keybindIndex >= 0)
                lines.Insert(keybindIndex, baseline);
            else
                lines.Add(baseline);
        }

        File.WriteAllLines(path, lines);
    }

    private static void InsertUe4ssModTxtEntry(List<string> lines, string modName, bool enabled)
    {
        var entry = $"{modName} : {(enabled ? "1" : "0")}";
        var keybindCommentIndex = lines.FindIndex(line =>
            line.TrimStart().StartsWith("; Built-in keybinds", StringComparison.OrdinalIgnoreCase));

        if (keybindCommentIndex >= 0)
            lines.Insert(keybindCommentIndex, entry);
        else
        {
            var keybindIndex = lines.FindIndex(line =>
                TryParseUe4ssModsTxtLine(line, out var name, out _) &&
                name.Equals("Keybinds", StringComparison.OrdinalIgnoreCase));

            if (keybindIndex >= 0) lines.Insert(keybindIndex, entry);
            else lines.Add(entry);
        }
    }

    private static void RemoveUe4ssModsTxtEntry(string modsRoot, string modName)
    {
        if (Ue4ssDefaultModNames.Contains(modName))
            return;

        var path = GetUe4ssModsTxtPath(modsRoot);
        if (!File.Exists(path)) return;

        var lines = File.ReadAllLines(path).ToList();
        var changed = lines.RemoveAll(line =>
            TryParseUe4ssModsTxtLine(line, out var name, out _) &&
            string.Equals(name, modName, StringComparison.OrdinalIgnoreCase)) > 0;

        if (changed)
            File.WriteAllLines(path, lines);
    }

    private static void EnsureUe4ssModsTxtMatchesInstalledMods(string modsRoot, string gameRoot)
    {
        EnsureUe4ssModsTxtFile(modsRoot);

        var installed = Directory.Exists(modsRoot)
            ? Directory.EnumerateDirectories(modsRoot, "*", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Where(name => !IsUe4ssSpecialFolderName(name!))
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var path = GetUe4ssModsTxtPath(modsRoot);
        var lines = File.ReadAllLines(path).ToList();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var changed = false;

        // Stale USER entries are removed so mods.txt matches installed mods.
        // Protected supplied entries are never removed.
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            if (!TryParseUe4ssModsTxtLine(lines[i], out var name, out _))
                continue;

            if (Ue4ssDefaultModNames.Contains(name))
            {
                seen.Add(name);
                continue;
            }

            if (!installed.Contains(name))
            {
                lines.RemoveAt(i);
                changed = true;
            }
            else
            {
                seen.Add(name);
            }
        }

        // Installed mods missing from the file are added disabled.
        foreach (var modName in installed.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (seen.Contains(modName)) continue;
            InsertUe4ssModTxtEntry(lines, modName, false);
            seen.Add(modName);
            changed = true;
        }

        if (changed)
            File.WriteAllLines(path, lines);
    }


    private void ModDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not ModEntry mod) return;
        if (mod.IsUe4ssDefault)
        {
            MessageBox.Show(this,
                L("'{0}' is a protected UE4SS default mod and cannot be deleted by ModHub.", mod.Name),
                L("Protected UE4SS Default Mod"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var result = MessageBox.Show(
            L("Delete mod '{0}'?\n\nThis cannot be undone.", mod.Name),
            L("Delete Mod"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;
        try
        {
            var gameRoot = GetVerifiedGameRoot();
            var data = LoadNexusMetadata();
            if (mod.IsPak)
            {
                var folder = GetPakModFamilyFolderForPath(mod.Path);
                var parent = Path.GetDirectoryName(folder);
                if (!string.Equals(parent, GetPakVirtualRoot(), StringComparison.OrdinalIgnoreCase) && Directory.Exists(folder))
                {
                    Directory.Delete(folder, true);
                    data.Remove(PakMetadataKey(mod.Path));
                    InvalidatePakConflictIndexForPath(folder);
                }
                else
                {
                    File.Delete(mod.Path);
                    data.Remove(PakMetadataKey(mod.Path));
                    InvalidatePakConflictIndexForPath(mod.Path);
                }
            }
            else
            {
                Directory.Delete(mod.Path, true);
                data.Remove(MetadataKey(gameRoot, mod.Path));
                RemoveUe4ssModsTxtEntry(
                    GetUe4ssModsRoot(gameRoot),
                    Path.GetFileName(mod.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
            }
            SaveNexusMetadata(data);
            RefreshModManager();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, L("Mod Manager"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static bool IsSupportedModArchive(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".zip", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".rar", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".rar5", StringComparison.OrdinalIgnoreCase);
    }

    private static string SupportedModArchiveFilter =>
        "Mod archives (*.zip;*.rar;*.rar5)|*.zip;*.rar;*.rar5|ZIP files (*.zip)|*.zip|RAR files (*.rar;*.rar5)|*.rar;*.rar5";

    private static IArchive OpenSupportedArchive(string archivePath)
    {
        try
        {
            return ArchiveFactory.OpenArchive(archivePath);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(
                "The mod archive could not be opened. ModHub supports standard ZIP archives, including ZIP files using LZMA compression, as well as other common archive formats supported by SharpCompress.", ex);
        }
    }

    private static void ExtractSupportedArchiveEntry(IArchiveEntry entry, string targetPath, bool overwrite = true)
    {
        var fullTarget = Path.GetFullPath(targetPath);
        var parent = Path.GetDirectoryName(fullTarget);
        if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
        if (!overwrite && File.Exists(fullTarget)) throw new IOException($"The file already exists: {fullTarget}");
        using var input = entry.OpenEntryStream();
        using var output = new FileStream(fullTarget, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.SequentialScan);
        input.CopyTo(output, 128 * 1024);
    }

    private static List<IArchiveEntry> GetSupportedArchiveEntries(IArchive archive)
    {
        return archive.Entries
            .Where(e => !e.IsDirectory && !string.IsNullOrWhiteSpace(e.Key))
            .ToList();
    }

    private async Task InstallPakFileAsync(string pakPath)
    {
        if (!File.Exists(pakPath))
            throw new FileNotFoundException(L("The selected PAK could not be found."), pakPath);

        var gameRoot = GetVerifiedGameRoot();
        var root = GetPakVirtualRoot();
        Directory.CreateDirectory(root);

        var sourceName = Path.GetFileName(pakPath);
        var baseName = Path.GetFileNameWithoutExtension(sourceName);
        var destinationFolder = Path.Combine(root, SanitizePakFolderName(baseName));
        Directory.CreateDirectory(destinationFolder);

        var destination = Path.Combine(destinationFolder, sourceName);
        if (File.Exists(destination))
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            destination = Path.Combine(destinationFolder, $"{baseName}_{stamp}.pak");
            var suffix = 1;
            while (File.Exists(destination))
                destination = Path.Combine(destinationFolder, $"{baseName}_{stamp}_{suffix++}.pak");
        }

        File.Copy(pakPath, destination, false);

        var metadata = LoadNexusMetadata();
        var installedMeta = new NexusModMetadata(baseName, "", 0, 0, sourceName)
        {
            InstalledVersion = "Unknown",
            LatestVersion = "Unknown"
        };
        metadata[PakMetadataKey(destination)] = installedMeta;
        WriteActivePakManifest(destination, installedMeta, baseName, "Unknown", sourceName);

        var order = GetOrderedPakPaths();
        if (!order.Contains(destination, StringComparer.OrdinalIgnoreCase))
        {
            order.Add(Path.GetFullPath(destination));
            SavePakLoadOrder(order);
        }

        SaveNexusMetadata(metadata);
        _cachedPakMods = null;
        _modCacheUpdatedUtc = DateTime.MinValue;
        RefreshModManager();
        await Task.CompletedTask;
    }

    private async Task InstallPakOrZipFilesAsync(IEnumerable<string> paths, bool pakPanel)
    {
        var valid = paths
            .Where(File.Exists)
            .Where(p => pakPanel
                ? Path.GetExtension(p).Equals(".pak", StringComparison.OrdinalIgnoreCase) || IsSupportedModArchive(p)
                : IsSupportedModArchive(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (valid.Count == 0)
        {
            MessageBox.Show(this,
                pakPanel ? L("Select a .pak or supported archive file.") : L("Select a supported mod archive file."),
                L("Install Mod"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SetOperationBusy(true, L("Installing mod…"));
        try
        {
            foreach (var path in valid)
            {
                if (pakPanel && Path.GetExtension(path).Equals(".pak", StringComparison.OrdinalIgnoreCase))
                    await InstallPakFileAsync(path);
                else
                    await InstallModZipAsync(path);
            }

            InvalidateDownloadsCache();
            RefreshDownloadsPage();
            RefreshModManager();
        }
        finally
        {
            SetOperationBusy(false);
        }
    }

    private void PakInstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (_operationBusy || _gameActive) return;

        var dialog = new OpenFileDialog
        {
            Title = L("Install PAK/Archive"),
            Filter = "PAK or supported archives (*.pak;*.zip;*.rar;*.rar5)|*.pak;*.zip;*.rar;*.rar5|PAK files (*.pak)|*.pak|" + SupportedModArchiveFilter,
            Multiselect = true,
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == true)
            _ = InstallPakOrZipFilesAsync(dialog.FileNames, true);
    }

    private void Ue4ssInstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (_operationBusy || _gameActive) return;

        var dialog = new OpenFileDialog
        {
            Title = L("Install Mod Archive"),
            Filter = SupportedModArchiveFilter,
            Multiselect = true,
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == true)
            _ = InstallPakOrZipFilesAsync(dialog.FileNames, false);
    }

    private void PakModsList_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var files = e.Data.GetData(DataFormats.FileDrop) as string[] ?? Array.Empty<string>();
        e.Effects = files.Any(f =>
            Path.GetExtension(f).Equals(".pak", StringComparison.OrdinalIgnoreCase) ||
            IsSupportedModArchive(f))
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Ue4ssModsList_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var files = e.Data.GetData(DataFormats.FileDrop) as string[] ?? Array.Empty<string>();
        e.Effects = files.Any(IsSupportedModArchive)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void PakModsList_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var files = e.Data.GetData(DataFormats.FileDrop) as string[] ?? Array.Empty<string>();
        e.Handled = true;
        if (_operationBusy || _gameActive) return;

        try
        {
            await InstallPakOrZipFilesAsync(files, true);
        }
        catch (Exception ex)
        {
            SetOperationBusy(false);
            MessageBox.Show(this, ex.Message, L("Install Mod"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Ue4ssModsList_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var files = e.Data.GetData(DataFormats.FileDrop) as string[] ?? Array.Empty<string>();
        e.Handled = true;
        if (_operationBusy || _gameActive) return;

        try
        {
            await InstallPakOrZipFilesAsync(files, false);
        }
        catch (Exception ex)
        {
            SetOperationBusy(false);
            MessageBox.Show(this, ex.Message, L("Install Mod"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task InstallModZipAsync(string zipPath, string? displayName = null, string? nexusGame = null, int nexusModId = 0, int nexusFileId = 0, PakInstallChoice installChoice = PakInstallChoice.Automatic)
    {
        if (!File.Exists(zipPath)) throw new FileNotFoundException(L("The selected mod archive could not be found."), zipPath);
        var gameRoot = GetVerifiedGameRoot();
        using var archive = OpenSupportedArchive(zipPath);
        var entries = GetSupportedArchiveEntries(archive);
        if (entries.Count == 0) throw new InvalidOperationException(L("The archive contains no files."));
        foreach (var entry in entries) ValidateZipEntry(entry.Key);

        var pakEntries = entries.Where(e => e.Key.EndsWith(".pak", StringComparison.OrdinalIgnoreCase)).ToList();
        var ueEntries = entries.Where(e => !e.Key.EndsWith(".pak", StringComparison.OrdinalIgnoreCase)).ToList();
        var metadata = LoadNexusMetadata();
        var managerName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        if (pakEntries.Count > 0)
        {
            var root = GetPakVirtualRoot();
            Directory.CreateDirectory(root);
            var downloadMetadata = metadata.GetValueOrDefault("_download:" + Path.GetFileName(zipPath));
            var requestedVersion = downloadMetadata?.LatestVersion;
            if (string.IsNullOrWhiteSpace(requestedVersion)) requestedVersion = "Unknown";
            var baseModName = string.IsNullOrWhiteSpace(managerName) ? Path.GetFileNameWithoutExtension(pakEntries[0].Key) : managerName!;
            var nexusCurrentFileCount = downloadMetadata?.NexusCurrentFileCount ?? -1;
            if (nexusCurrentFileCount < 0 && (nexusModId > 0 || (downloadMetadata?.ModId ?? 0) > 0))
            {
                try
                {
                    nexusCurrentFileCount = await FetchNexusCurrentFileCountAsync(
                        nexusGame ?? downloadMetadata?.Game ?? "",
                        nexusModId != 0 ? nexusModId : (downloadMetadata?.ModId ?? 0));
                }
                catch { }
            }
            if (installChoice == PakInstallChoice.Automatic && nexusCurrentFileCount > 1)
                installChoice = ShowNexusMultiFileInstallChoice(baseModName, Path.GetFileName(pakEntries[0].Key), nexusCurrentFileCount);
            if (installChoice == PakInstallChoice.Automatic && pakEntries.Count > 1)
                installChoice = ShowNexusMultiFileInstallChoice(baseModName, Path.GetFileName(pakEntries[0].Key), pakEntries.Count);
            if (installChoice == PakInstallChoice.Cancel) return;
            if (installChoice == PakInstallChoice.Automatic) installChoice = PakInstallChoice.UpdateExisting;

            var familyFolder = FindExistingPakModFamilyFolder(baseModName, nexusGame ?? downloadMetadata?.Game, nexusModId != 0 ? nexusModId : (downloadMetadata?.ModId ?? 0), metadata)
                               ?? Path.Combine(root, SanitizePakFolderName(baseModName));
            if (nexusCurrentFileCount > 1 || installChoice == PakInstallChoice.AddAsNew || pakEntries.Count > 1)
            {
                Directory.CreateDirectory(familyFolder);
                EnsurePakFamilyPackageLayout(familyFolder, baseModName, metadata);
            }

            foreach (var entry in pakEntries)
            {
                var originalPakName = Path.GetFileName(entry.Key.Replace('/', Path.DirectorySeparatorChar));
                if (string.IsNullOrWhiteSpace(originalPakName)) continue;
                var modName = (installChoice == PakInstallChoice.AddAsNew &&
                               (nexusCurrentFileCount > 1 || pakEntries.Count > 1))
                    ? $"{baseModName} - {Path.GetFileNameWithoutExtension(originalPakName)}"
                    : baseModName;
                var familyPath = familyFolder;
                var effectiveModId = nexusModId != 0 ? nexusModId : (downloadMetadata?.ModId ?? 0);
                var effectiveGame = nexusGame ?? downloadMetadata?.Game;
                var effectiveFileId = nexusFileId != 0 ? nexusFileId : (downloadMetadata?.FileId ?? 0);
                string modFolder;
                if (nexusCurrentFileCount > 1 || installChoice == PakInstallChoice.AddAsNew || pakEntries.Count > 1)
                {
                    var existingPackage = installChoice == PakInstallChoice.UpdateExisting
                        ? FindExistingPakPackageFolder(familyPath, effectiveGame, effectiveModId, effectiveFileId, metadata)
                        : null;
                    if (!string.IsNullOrWhiteSpace(existingPackage))
                    {
                        modFolder = existingPackage!;
                    }
                    else
                    {
                        var packageBase = SanitizePakFolderName(Path.GetFileNameWithoutExtension(originalPakName));
                        if (string.IsNullOrWhiteSpace(packageBase)) packageBase = "addon";
                        if (installChoice == PakInstallChoice.AddAsNew)
                        {
                            var candidate = packageBase;
                            var index = 1;
                            while (Directory.Exists(Path.Combine(familyPath, candidate))) candidate = packageBase + "_addon" + index++;
                            packageBase = candidate;
                        }
                        modFolder = Path.Combine(familyPath, packageBase);
                    }
                }
                else
                {
                    modFolder = familyPath;
                }
                Directory.CreateDirectory(modFolder);
                var versionsFolder = Path.Combine(modFolder, "_versions");
                Directory.CreateDirectory(versionsFolder);
                var nowUtc = DateTime.UtcNow;
                var activeFileName = $"{GetPakVersionTimestamp(nowUtc)}_{SanitizePakVersionPart(requestedVersion)}.pak";
                var activePath = Path.Combine(modFolder, activeFileName);
                while (File.Exists(activePath))
                    activePath = Path.Combine(modFolder, $"{GetPakVersionTimestamp(DateTime.UtcNow)}_{SanitizePakVersionPart(requestedVersion)}_{Guid.NewGuid().ToString("N")[..6]}.pak");

                var existingActive = GetActivePakInModFolder(modFolder);
                var preserveEnabled = false;
                if (existingActive != null)
                {
                    var existingFileName = Path.GetFileName(existingActive);
                    var enabledTarget = Path.Combine(GetPakModsRoot(gameRoot), existingFileName);
                    preserveEnabled = File.Exists(enabledTarget) || IsSymbolicLink(enabledTarget);
                    var disabledTarget = enabledTarget + ".RRModHub.DISABLED";
                    try { if (File.Exists(enabledTarget) || IsSymbolicLink(enabledTarget)) File.Delete(enabledTarget); } catch { }
                    try { if (File.Exists(disabledTarget)) File.Delete(disabledTarget); } catch { }

                    var existingJson = GetJsonForPak(existingActive);
                    var destinationPak = Path.Combine(versionsFolder, Path.GetFileName(existingActive));
                    var destinationJson = Path.Combine(versionsFolder, Path.GetFileName(existingJson));
                    CopyOrMoveFile(existingActive, destinationPak);
                    if (File.Exists(existingJson)) CopyOrMoveFile(existingJson, destinationJson);
                    metadata.Remove(PakMetadataKey(existingActive));
                    InvalidatePakConflictIndexForPath(existingActive);
                }

                ExtractSupportedArchiveEntry(entry, activePath, true);
                var installedMeta = new NexusModMetadata(
                    modName, nexusGame ?? downloadMetadata?.Game ?? "", nexusModId != 0 ? nexusModId : (downloadMetadata?.ModId ?? 0),
                    nexusFileId != 0 ? nexusFileId : (downloadMetadata?.FileId ?? 0), Path.GetFileName(zipPath))
                {
                    InstalledVersion = requestedVersion,
                    LatestVersion = downloadMetadata?.LatestVersion ?? requestedVersion,
                    Description = downloadMetadata?.Description ?? "",
                    FilesCount = downloadMetadata?.FilesCount ?? -1,
                    NexusCurrentFileCount = downloadMetadata?.NexusCurrentFileCount ?? -1,
                    DownloadedAtUtc = downloadMetadata?.DownloadedAtUtc
                };
                metadata[PakMetadataKey(activePath)] = installedMeta;
                WriteActivePakManifest(activePath, installedMeta, modName, requestedVersion, originalPakName);
                if (existingActive != null)
                    SetPakPathEnabled(gameRoot, activePath, preserveEnabled);
            }
            SaveNexusMetadata(metadata);
            _cachedPakMods = null;
            _modCacheUpdatedUtc = DateTime.MinValue;
            if (!_gameActive && _mode == "conflicts") RefreshConflictCheckPage();
            return;
        }

        if (ueEntries.Count == 0) throw new InvalidOperationException(L("The archive could not be identified as a PAK or UE4SS mod."));
        var modRootName = DetectUe4ssModRoot(ueEntries) ?? Path.GetFileNameWithoutExtension(zipPath);
        var ueModsRoot = GetUe4ssModsRoot(gameRoot);
        var destinationRoot = Path.Combine(ueModsRoot, modRootName);
        var wasInstalled = Directory.Exists(destinationRoot);
        var preserveUe4ssEnabled = wasInstalled
            ? ReadUe4ssModsTxtEnabled(ueModsRoot, modRootName, defaultValue: false)
            : false;

        if (wasInstalled)
        {
            var answer = MessageBox.Show(L("The UE4SS mod '{0}' already exists. Replace it?", modRootName), L("Install Mod"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
            Directory.Delete(destinationRoot, true);
        }

        Directory.CreateDirectory(destinationRoot);
        foreach (var entry in ueEntries)
        {
            var relative = StripDetectedModRoot(entry.Key, modRootName);
            if (string.IsNullOrWhiteSpace(relative)) continue;

            // ModHub claims UE4SS enabled markers instead of deleting them.
            // The controlled suffix also lets the running manager lock the file
            // against external edits/renames.
            if (string.Equals(Path.GetFileName(relative), "enabled.txt", StringComparison.OrdinalIgnoreCase))
                relative += ".RRModHub.CONTROLLED";

            var target = Path.GetFullPath(Path.Combine(destinationRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
            var rootFull = Path.GetFullPath(destinationRoot + Path.DirectorySeparatorChar);
            if (!target.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(L("The archive contains an unsafe path and was not installed."));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            ExtractSupportedArchiveEntry(entry, target, true);
        }

        EnsureUe4ssModsTxtFile(ueModsRoot);
        SetUe4ssModsTxtEnabled(ueModsRoot, modRootName, preserveUe4ssEnabled);
        if (!string.IsNullOrWhiteSpace(managerName))
        {
            var downloadMetadata = metadata.GetValueOrDefault("_download:" + Path.GetFileName(zipPath));
            var installedUeMeta = new NexusModMetadata(managerName, nexusGame ?? "", nexusModId, nexusFileId, Path.GetFileName(zipPath))
            { InstalledVersion = downloadMetadata?.LatestVersion ?? "", LatestVersion = downloadMetadata?.LatestVersion ?? "", Description = downloadMetadata?.Description ?? "", FilesCount = downloadMetadata?.FilesCount ?? -1, DownloadedAtUtc = downloadMetadata?.DownloadedAtUtc };
            metadata[MetadataKey(gameRoot, destinationRoot)] = installedUeMeta;
        }
        SaveNexusMetadata(metadata);
        await Task.CompletedTask;
    }

    internal void ShowFromExternalRequest()
    {
        try
        {
            if (!IsVisible) Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            ShowInTaskbar = true;
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        }
        catch { }
    }

    internal async Task HandleNexusUriAsync(string uriText)
    {
        try
        {
            if (!Uri.TryCreate(uriText, UriKind.Absolute, out var uri) || !string.Equals(uri.Scheme, "nxm", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(L("The Nexus Mods link is invalid."));
            // Nexus nxm:// download links use the game domain as the URI host:
            // nxm://<game>/mods/<modId>/files/<fileId>?key=...&expires=...
            // Older/alternate forms may put the game in the path, so accept both.
            var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            string? game = null;
            int modId;
            int fileId;

            if (parts.Length >= 4
                && parts[0].Equals("mods", StringComparison.OrdinalIgnoreCase)
                && parts[2].Equals("files", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(uri.Host)
                && int.TryParse(parts[1], out modId)
                && int.TryParse(parts[3], out fileId))
            {
                game = uri.Host;
            }
            else if (parts.Length >= 5
                && parts[1].Equals("mods", StringComparison.OrdinalIgnoreCase)
                && parts[3].Equals("files", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(parts[2], out modId)
                && int.TryParse(parts[4], out fileId))
            {
                game = parts[0];
            }
            else
            {
                throw new InvalidOperationException(L("The Nexus Mods link is not a supported mod download link."));
            }

            if (string.IsNullOrWhiteSpace(game))
                throw new InvalidOperationException(L("The Nexus Mods link does not contain a valid Nexus game."));

            var query = ParseNexusQuery(uri.Query);
            var oneTimeKey = query.GetValueOrDefault("key");
            var expires = query.GetValueOrDefault("expires");
            var userId = query.GetValueOrDefault("user_id");
            var apiKey = string.IsNullOrWhiteSpace(_nexusApiKey) ? NexusSecretStore.Load() : _nexusApiKey;
            if (string.IsNullOrWhiteSpace(apiKey) && string.IsNullOrWhiteSpace(oneTimeKey))
                throw new InvalidOperationException(L("Nexus did not provide a usable download key. Connect Nexus in Settings or start the download again from the Nexus Mods Download with Mod Manager button."));

            var request = new NexusFileDownloadRequest(
                game, modId, fileId, $"NexusMod_{modId}_{fileId}.zip", "Unknown", -1,
                oneTimeKey, expires, userId);
            await DownloadNexusFileAsync(request);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, L("Install Mod"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task<NexusModInfo?> FetchNexusModInfoAsync(string game, int modId, CancellationToken cancellationToken = default)
    {
        var apiKey = string.IsNullOrWhiteSpace(_nexusApiKey) ? NexusSecretStore.Load() : _nexusApiKey;
        if (string.IsNullOrWhiteSpace(apiKey)) return null;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RetroRewindModHub/1.0.0");
        client.DefaultRequestHeaders.TryAddWithoutValidation("apikey", apiKey);
        var info = new NexusModInfo();
        var url = $"https://api.nexusmods.com/v1/games/{Uri.EscapeDataString(game)}/mods/{modId}";
        using var response = await client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        info = new NexusModInfo
        {
            Name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
            Version = root.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "",
            Description = root.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
            Author = root.TryGetProperty("author", out var a) ? a.GetString() ?? "" : ""
        };
        SaveNexusDescriptionCache(game, modId, info.Name, info.Version, info.Description);
        return info;
    }

    private static NexusModMetadata ApplyNexusInfo(NexusModMetadata meta, NexusModInfo info) => meta with
    {
        Name = string.IsNullOrWhiteSpace(info.Name) ? meta.Name : info.Name,
        LatestVersion = info.Version,
        Description = info.Description,
        FilesCount = info.FilesCount,
        Author = info.Author,
    };

    private async Task RefreshLinkedNexusMetadataIfDueAsync()
    {
        if (_gameActive || _nexusBackgroundRefreshInProgress) return;
        if (DateTime.UtcNow - _nexusLastBackgroundRefreshUtc < BackgroundMetadataRefreshInterval) return;
        _nexusBackgroundRefreshInProgress = true;
        try
        {
        try { _nexusBackgroundCts?.Cancel(); } catch { }
        var nexusCts = new CancellationTokenSource();
        _nexusBackgroundCts = nexusCts;
        var apiKey = string.IsNullOrWhiteSpace(_nexusApiKey) ? NexusSecretStore.Load() : _nexusApiKey;
        if (string.IsNullOrWhiteSpace(apiKey)) return;
        var data = LoadNexusMetadata();
        var changed = false;
        foreach (var key in data.Keys.Where(k => !k.StartsWith("_download:", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            var meta = data[key];
            if (meta.ModId <= 0 || string.IsNullOrWhiteSpace(meta.Game)) continue;
            try
            {
                var info = await FetchNexusModInfoAsync(meta.Game, meta.ModId, nexusCts.Token);
                if (info == null) continue;
                var updated = ApplyNexusInfo(meta, info);
                if (string.IsNullOrWhiteSpace(updated.InstalledVersion) && !string.IsNullOrWhiteSpace(meta.InstalledVersion)) updated = updated with { InstalledVersion = meta.InstalledVersion };
                data[key] = updated;
                changed = true;
            }
            catch { }
        }
        if (changed)
        {
            SaveNexusMetadata(data);
            if (_mode == "mods" && _cachedPakMods != null && _cachedUe4ssMods != null && _cachedPendingMods != null)
                ApplyModManagerSnapshot(_cachedPakMods, _cachedUe4ssMods, _cachedPendingMods);
        }

        _nexusLastBackgroundRefreshUtc = DateTime.UtcNow;
        }
        finally
        {
            _nexusBackgroundRefreshInProgress = false;
        }
    }

    private static void ValidateZipEntry(string fullName)
    {
        var normalized = fullName.Replace('\\', '/');
        if (normalized.StartsWith("/", StringComparison.Ordinal) ||
            normalized.Split('/').Any(part => part == "..") ||
            Path.IsPathRooted(normalized))
            throw new InvalidOperationException("The ZIP contains an unsafe path and was rejected.");
    }

    private static string? DetectUe4ssModRoot(IEnumerable<IArchiveEntry> entries)
    {
        foreach (var entry in entries)
        {
            var parts = entry.Key.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < parts.Length - 1; i++)
            {
                if (parts[i].Equals("Mods", StringComparison.OrdinalIgnoreCase) && i + 1 < parts.Length)
                    return parts[i + 1];
            }
        }
        var first = entries.FirstOrDefault()?.Key.Replace('\\', '/');
        if (first == null) return null;
        var root = first.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(root) || root.Contains('.') ? null : root;
    }

    private static string StripDetectedModRoot(string fullName, string modRootName)
    {
        var normalized = fullName.Replace('\\', '/').TrimStart('/');
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i].Equals("Mods", StringComparison.OrdinalIgnoreCase) &&
                parts[i + 1].Equals(modRootName, StringComparison.OrdinalIgnoreCase))
                return string.Join('/', parts[(i + 2)..]);
        }
        if (parts.Length > 1 && parts[0].Equals(modRootName, StringComparison.OrdinalIgnoreCase))
            return string.Join('/', parts[1..]);
        return normalized;
    }

    private static void Require(string path) { if(!File.Exists(path)) throw new Exception("Select a valid file."); }

    private static string Q(string s) => "\""+s.Replace("\"","\\\"")+"\"";

    private static string Pretty(string stdout, string output, string? backupPath = null)
    {
        try
        {
            using var doc=JsonDocument.Parse(stdout);
            var root=doc.RootElement;
            var count = root.TryGetProperty("items", out var items) && items.TryGetInt32(out var itemCount)
                ? itemCount
                : (root.TryGetProperty("count", out var c) && c.TryGetInt32(out var legacyCount) ? legacyCount : 0);
            var backupText = string.IsNullOrWhiteSpace(backupPath)
                ? ""
                : $"\n\nOriginal save backed up as:\n{backupPath}";
            return $"Completed successfully.\n\nTransferred objects: {count}\n\nOutput:\n{output}{backupText}";
        }
        catch
        {
            var backupText = string.IsNullOrWhiteSpace(backupPath)
                ? ""
                : $"\n\nOriginal save backed up as:\n{backupPath}";
            return stdout.Length>0
                ? stdout + backupText
                : $"Completed successfully.\n\nOutput:\n{output}{backupText}";
        }
    }

    private void UpdateUe4ssSharedScriptsButtons()
    {
        try
        {
            string modsRoot = GetUe4ssModsRoot(GetVerifiedGameRoot());
            string sharedPath = Path.Combine(modsRoot, "shared");
            string scriptsPath = Path.Combine(modsRoot, "Scripts");

            if (Ue4ssSharedButton != null)
                Ue4ssSharedButton.IsEnabled = Directory.Exists(sharedPath);

            if (Ue4ssScriptsButton != null)
                Ue4ssScriptsButton.IsEnabled = Directory.Exists(scriptsPath);
        }
        catch
        {
            if (Ue4ssSharedButton != null)
                Ue4ssSharedButton.IsEnabled = false;
            if (Ue4ssScriptsButton != null)
                Ue4ssScriptsButton.IsEnabled = false;
        }
    }

    private void Ue4ssSharedButton_Click(object sender, RoutedEventArgs e)
    {
        OpenUe4ssSpecialFolder("shared");
    }

    private void Ue4ssScriptsButton_Click(object sender, RoutedEventArgs e)
    {
        OpenUe4ssSpecialFolder("Scripts");
    }

    private void OpenUe4ssSpecialFolder(string folderName)
    {
        try
        {
            string path = Path.Combine(GetUe4ssModsRoot(GetVerifiedGameRoot()), folderName);
            if (!Directory.Exists(path))
                return;

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "\"" + path + "\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Unable to open the UE4SS " + folderName + " folder.\n\n" + ex.Message,
                "Retro Rewind ModHub",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static bool IsUe4ssSpecialFolderName(string name)
    {
        return string.Equals(name, "shared", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Scripts", StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateUe4ssSpecialFoldersButtons()
    {
        try
        {
            if (Ue4ssSpecialFoldersPanel != null)
                Ue4ssSpecialFoldersPanel.Visibility =
                    _showUe4ssDefaultMods ? Visibility.Visible : Visibility.Collapsed;

            UpdateUe4ssSharedScriptsButtons();
        }
        catch
        {
            if (Ue4ssSpecialFoldersPanel != null)
                Ue4ssSpecialFoldersPanel.Visibility = Visibility.Collapsed;
            if (Ue4ssSharedButton != null)
                Ue4ssSharedButton.IsEnabled = false;
            if (Ue4ssScriptsButton != null)
                Ue4ssScriptsButton.IsEnabled = false;
        }
    }

    private void SetUe4ssSpecialFoldersVisibility(bool visible)
    {
        _showUe4ssDefaultMods = visible;
        UpdateUe4ssSpecialFoldersButtons();
    }

    private object ResolveChangeNameTarget(object commandTarget)
    {
        if (commandTarget != null)
            return commandTarget;

        return null;
    }

    private IDisposable BeginBulkSymbolicLinkOperation()
    {
        _bulkSymbolicLinkOperation = true;
        return new BulkSymbolicLinkScope(() => _bulkSymbolicLinkOperation = false);
    }
}
