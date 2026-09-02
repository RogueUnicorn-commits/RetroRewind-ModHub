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
    private void ApplyTheme(bool dark)
    {
        // Retro Rewind core UI palette. Keep the interface consistently
        // dark navy/teal with warm cream typography; the 80s neon palette
        // remains reserved for the application artwork.
        Resources["SecondaryCardBrush"] = Brush("#14212A");
        Resources["WindowBackgroundBrush"] = Brush("#0A0E17");
        Resources["ForegroundBrush"] = Brush("#FEE1B5");
        Resources["LabelBrush"] = Brush("#415151");
        Resources["SecondaryBrush"] = Brush("#415151");
        Resources["CardBrush"] = Brush("#111F28");
        Resources["BorderBrush"] = Brush("#263030");
        Resources["SeparatorBrush"] = Brush("#263030");
        Resources["InputBackgroundBrush"] = Brush("#14212A");

        Resources["ButtonBackgroundBrush"] = Brush("#0F1D26");
        Resources["AccentBrush"] = Brush("#125F6F");
        Resources["AccentHoverBrush"] = Brush("#146272");
        Resources["AccentPressedBrush"] = Brush("#0E3846");
        Resources["AccentFocusBrush"] = Brush("#146272");
        Resources["AccentForegroundBrush"] = Brush("#FEE1B5");
        Resources["SidebarIconBrush"] = Brush("#FEE1B5");

        Resources["TabBackgroundBrush"] = Brush("#0F1D26");
        Resources["TabForegroundBrush"] = Brush("#FEDDAA");
        Resources["CheckForegroundBrush"] = Brush("#FEE1B5");
    }

    private static Color GetWindowsAccentColor()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\DWM");

            if (key?.GetValue("ColorizationColor") is int value)
            {
                uint u = unchecked((uint)value);
                return Color.FromArgb(
                    0xFF,
                    (byte)((u >> 16) & 0xFF),
                    (byte)((u >> 8) & 0xFF),
                    (byte)(u & 0xFF));
            }
        }
        catch
        {
            // Fall through to the Windows-default accent.
        }

        return (Color)ColorConverter.ConvertFromString("#0078D4");
    }

    private static Color AdjustAccent(Color c, double factor)
    {
        byte Scale(byte value)
        {
            return (byte)Math.Clamp((int)Math.Round(value * factor), 0, 255);
        }

        return Color.FromArgb(0xFF, Scale(c.R), Scale(c.G), Scale(c.B));
    }

    private static Color GetContrastingForeground(Color c)
    {
        double luminance =
            (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

        return luminance >= 0.62 ? Colors.Black : Colors.White;
    }

    private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));

    private static string Info(string path, string expected)
    {
        if (!File.Exists(path)) return $"Select a {expected} file.";
        return $"{new FileInfo(path).Length / 1048576.0:0.00} MB";
    }

    private void SetSidebarButtonWidths(double sidebarWidth)
    {
        bool expanded = sidebarWidth > SidebarCollapsedWidth + 10;
        double buttonWidth = expanded ? Math.Max(30, sidebarWidth - 10) : 30;

        SidebarToggleButton.Width = expanded ? Math.Max(30, buttonWidth) : 30;
        HomeButton.Width = buttonWidth;
        LaunchGameButton.Width = buttonWidth;
        ModManagerGroupButton.Width = buttonWidth;
        SaveManagerGroupButton.Width = buttonWidth;
        ModManagerTab.Width = Math.Max(30, buttonWidth - 14);
        MergeModsTab.Width = Math.Max(30, buttonWidth - 14);
        ConfigureModsTab.Width = Math.Max(30, buttonWidth - 14);
        AssetWorkshopTab.Width = Math.Max(30, buttonWidth - 14);
        VideosTab.Width = Math.Max(30, buttonWidth - 14);
        RequiredFilesTab.Width = Math.Max(30, buttonWidth - 14);
        VideoEditorTab.Width = Math.Max(30, buttonWidth - 14);
        ConflictCheckTab.Width = Math.Max(30, buttonWidth - 14);
        TransferTab.Width = Math.Max(30, buttonWidth - 14);
        ExportTab.Width = Math.Max(30, buttonWidth - 14);
        ImportTab.Width = Math.Max(30, buttonWidth - 14);
        InfoTab.Width = Math.Max(30, buttonWidth - 14);
        HealthCheckTab.Width = Math.Max(30, buttonWidth - 14);
        StoreManagementTab.Width = Math.Max(30, buttonWidth - 14);
        OpenSaveFolderButton.Width = Math.Max(30, buttonWidth - 14);
        DownloadsButton.Width = buttonWidth;
        AboutButton.Width = buttonWidth;
        SettingsButton.Width = buttonWidth;

        var alignment = expanded ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        SidebarItemsPanel.HorizontalAlignment = alignment;
        ModManagerChildrenPanel.HorizontalAlignment = alignment;
        SaveManagerChildrenPanel.HorizontalAlignment = alignment;
        SettingsItemsPanel.HorizontalAlignment = alignment;
        DownloadsItemsPanel.HorizontalAlignment = alignment;
        AboutItemsPanel.HorizontalAlignment = alignment;

        // Keep Home deterministic: the label must follow the actual sidebar width,
        // even when the expanded state is reached through layout/startup restoration.
        HomeButtonText.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;

        SetSidebarButtonAlignment(HomeButton, expanded);
        SetSidebarButtonAlignment(LaunchGameButton, expanded);
        SetSidebarButtonAlignment(ModManagerGroupButton, expanded);
        SetSidebarButtonAlignment(SaveManagerGroupButton, expanded);
        SetSidebarButtonAlignment(ModManagerTab, expanded);
        SetSidebarButtonAlignment(MergeModsTab, expanded);
        SetSidebarButtonAlignment(ConfigureModsTab, expanded);
        SetSidebarButtonAlignment(AssetWorkshopTab, expanded);
        SetSidebarButtonAlignment(VideosTab, expanded);
        SetSidebarButtonAlignment(RequiredFilesTab, expanded);
        SetSidebarButtonAlignment(VideoEditorTab, expanded);
        SetSidebarButtonAlignment(ConflictCheckTab, expanded);
        SetSidebarButtonAlignment(TransferTab, expanded);
        SetSidebarButtonAlignment(ExportTab, expanded);
        SetSidebarButtonAlignment(ImportTab, expanded);
        SetSidebarButtonAlignment(InfoTab, expanded);
        SetSidebarButtonAlignment(HealthCheckTab, expanded);
        SetSidebarButtonAlignment(StoreManagementTab, expanded);
        SetSidebarButtonAlignment(OpenSaveFolderButton, expanded);
        SetSidebarButtonAlignment(SettingsButton, expanded);
        SetSidebarButtonAlignment(DownloadsButton, expanded);
        SetSidebarButtonAlignment(AboutButton, expanded);
        SetSidebarButtonAlignment(SidebarToggleButton, expanded);
    }

    private static void SetSidebarButtonAlignment(Button button, bool expanded)
    {
        button.HorizontalContentAlignment = expanded ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        SetSidebarIconOffset(button, expanded);
    }

    private static void SetSidebarIconOffset(Button button, bool expanded)
    {
        if (button.Content is Panel panel && panel.Children.Count > 0 &&
            panel.Children[0] is Border icon)
        {
            icon.Margin = expanded ? new Thickness(10, 0, 0, 0) : new Thickness(0);
        }
    }

    private void SidebarToggle_Click(object sender, RoutedEventArgs e)
    {
        _sidebarExpanded = !_sidebarExpanded;
        _sidebarAnimationTimer?.Stop();
        _sidebarAnimationStart = SidebarColumn.Width.Value;
        _sidebarAnimationTarget = _sidebarExpanded ? SidebarExpandedWidth : SidebarCollapsedWidth;
        _sidebarAnimationStarted = DateTime.UtcNow;
        if (_sidebarExpanded) SetSidebarLabelVisibility(Visibility.Visible);

        _sidebarAnimationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _sidebarAnimationTimer.Tick += SidebarAnimation_Tick;
        _sidebarAnimationTimer.Start();
    }

    private void SidebarAnimation_Tick(object? sender, EventArgs e)
    {
        const double durationMs = 240.0;
        double elapsed = (DateTime.UtcNow - _sidebarAnimationStarted).TotalMilliseconds;
        double t = Math.Clamp(elapsed / durationMs, 0.0, 1.0);
        double eased = t < 0.5 ? 4.0 * t * t * t : 1.0 - Math.Pow(-2.0 * t + 2.0, 3.0) / 2.0;
        double width = _sidebarAnimationStart + (_sidebarAnimationTarget - _sidebarAnimationStart) * eased;
        SidebarColumn.Width = new GridLength(width);
        SidebarPanel.Width = width;
        Sidebar.Width = width;
        SetSidebarButtonWidths(width);

        if (t >= 1.0)
        {
            SidebarColumn.Width = new GridLength(_sidebarAnimationTarget);
            SidebarPanel.Width = _sidebarAnimationTarget;
            Sidebar.Width = _sidebarAnimationTarget;
            SetSidebarButtonWidths(_sidebarAnimationTarget);
            _sidebarAnimationTimer?.Stop();
            _sidebarAnimationTimer = null;
            if (!_sidebarExpanded) SetSidebarLabelVisibility(Visibility.Collapsed);
        }
    }

    private void SetSidebarLabelVisibility(Visibility visibility)
    {
        HomeButtonText.Visibility = visibility;
        MenuButtonText.Visibility = visibility;
        LaunchGameButtonText.Visibility = visibility;
        ModManagerGroupText.Visibility = visibility;
        SaveManagerGroupText.Visibility = visibility;
        ModManagerTabText.Visibility = visibility;
        ConfigureModsTabText.Visibility = visibility;
        VideosTabText.Visibility = visibility;
        RequiredFilesTabText.Visibility = visibility;
        VideoEditorTabText.Visibility = visibility;
        ConflictCheckTabText.Visibility = visibility;
        TransferTabText.Visibility = visibility;
        ExportTabText.Visibility = visibility;
        ImportTabText.Visibility = visibility;
        InfoTabText.Visibility = visibility;
        HealthCheckTabText.Visibility = visibility;
        StoreManagementTabText.Visibility = visibility;
        OpenSaveFolderText.Visibility = visibility;
        DownloadsButtonText.Visibility = visibility;
        AboutButtonText.Visibility = visibility;
        AssetWorkshopTabText.Visibility = visibility;
        MergeModsTabText.Visibility = visibility;
        SettingsButtonText.Visibility = visibility;
    }

    private void SidebarGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender == ModManagerGroupButton)
        {
            bool wasExpanded = _modManagerExpanded;
            _modManagerExpanded = !_modManagerExpanded;
            AnimateSidebarGroup(ModManagerChildrenPanel, _modManagerExpanded, 7);

            if (wasExpanded && !_modManagerExpanded && IsModManagerPage(_mode))
            {
                RememberCurrentPaths();
                _mode = "home";
                UpdateMode();
                RestoreRememberedPaths();
            }
        }
        else if (sender == SaveManagerGroupButton)
        {
            bool wasExpanded = _saveManagerExpanded;
            _saveManagerExpanded = !_saveManagerExpanded;
            AnimateSidebarGroup(SaveManagerChildrenPanel, _saveManagerExpanded, 7);

            if (wasExpanded && !_saveManagerExpanded && IsSaveManagerPage(_mode))
            {
                RememberCurrentPaths();
                _mode = "home";
                UpdateMode();
                RestoreRememberedPaths();
            }
        }
    }

    private static bool IsModManagerPage(string mode) =>
        mode is "mods" or "mergemods" or "conflicts" or "configuremods" or "assets" or "asset_texture" or "asset_staticmesh" or "asset_skeletalmesh" or "asset_material" or "asset_animation" or "asset_audio" or "asset_blueprint" or "asset_niagara" or "asset_particle" or "asset_widget" or "asset_world" or "asset_other" or "videos" or "requiredfiles" or "videoeditor";

    private static bool IsSaveManagerPage(string mode) =>
        mode is "transfer" or "export" or "import" or "info" or "health" or "manage";

    private void AnimateSidebarGroup(StackPanel panel, bool expand, int childCount)
    {
        _sidebarGroupAnimationTimer?.Stop();
        _sidebarGroupAnimatingPanel = panel;
        _sidebarGroupAnimationStart = panel.Height;
        _sidebarGroupAnimationTarget = expand ? childCount * 34.0 : 0.0;
        _sidebarGroupAnimationStarted = DateTime.UtcNow;
        if (expand) SetSidebarLabelVisibility(_sidebarExpanded ? Visibility.Visible : Visibility.Collapsed);

        _sidebarGroupAnimationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _sidebarGroupAnimationTimer.Tick += SidebarGroupAnimation_Tick;
        _sidebarGroupAnimationTimer.Start();
    }

    private void SidebarGroupAnimation_Tick(object? sender, EventArgs e)
    {
        if (_sidebarGroupAnimatingPanel is null) return;
        const double durationMs = 220.0;
        double elapsed = (DateTime.UtcNow - _sidebarGroupAnimationStarted).TotalMilliseconds;
        double t = Math.Clamp(elapsed / durationMs, 0.0, 1.0);
        double eased = t < 0.5 ? 4.0 * t * t * t : 1.0 - Math.Pow(-2.0 * t + 2.0, 3.0) / 2.0;
        _sidebarGroupAnimatingPanel.Height = _sidebarGroupAnimationStart +
            (_sidebarGroupAnimationTarget - _sidebarGroupAnimationStart) * eased;

        if (t >= 1.0)
        {
            _sidebarGroupAnimatingPanel.Height = _sidebarGroupAnimationTarget;
            _sidebarGroupAnimationTimer?.Stop();
            _sidebarGroupAnimationTimer = null;
            _sidebarGroupAnimatingPanel = null;
        }
    }

    private sealed record SteamNewsItem(string Title, string Date, string Description, string Url);

    
private const uint RetroRewindSteamAppId = 3552140;

    private sealed class SteamHomeProfile
    {
        public string SteamId64 { get; init; } = "";
        public string AccountName { get; init; } = "";
        public string PersonaName { get; init; } = "";
        public string ProfileUrl { get; init; } = "";
        public string AvatarUrl { get; init; } = "";
    }

    private void LoadSteamHomeProfile()
    {
        var local = TryReadLocalSteamProfile();
        var cached = AccountProfileCache.LoadSteam();
        var profile = local ?? (cached == null ? null : new SteamHomeProfile
        {
            SteamId64 = cached.SteamId64,
            AccountName = cached.AccountName,
            PersonaName = cached.PersonaName,
            ProfileUrl = cached.ProfileUrl,
            AvatarUrl = cached.AvatarUrl
        });

        // Repair the encrypted account JSON whenever a local Steam profile is
        // available. This also restores the JSON cache if an older build left
        // only the encrypted avatar behind.
        if (local != null && !string.IsNullOrWhiteSpace(local.SteamId64))
        {
            try
            {
                AccountProfileCache.SaveSteam(new AccountProfileCache.SteamAccountCache
                {
                    SteamId64 = local.SteamId64,
                    AccountName = local.AccountName,
                    PersonaName = local.PersonaName,
                    ProfileUrl = local.ProfileUrl,
                    AvatarUrl = local.AvatarUrl,
                    LastUpdatedUtc = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                CrashLogger.Write("SteamAccountCacheRepair", ex);
            }
        }

        if (profile == null)
        {
            HomeSteamStatus.Text = L("Not connected");
            HomeSteamPersonaName.Text = L("Steam Profile");
            HomeSteamAccountName.Text = L("Sign in to Steam to display your profile.");
            HomeSteamId.Text = "";
            HomeSteamAvatarInitials.Text = "S";
            HomeSteamAvatarImage.Source = null;
            HomeSteamAvatarImage.Visibility = Visibility.Collapsed;
            HomeSteamAvatarInitials.Visibility = Visibility.Visible;
            return;
        }

        HomeSteamStatus.Text = L("Connected");
        HomeSteamPersonaName.Text = string.IsNullOrWhiteSpace(profile.PersonaName)
            ? profile.AccountName
            : profile.PersonaName;
        HomeSteamAccountName.Text = string.IsNullOrWhiteSpace(profile.AccountName)
            ? L("Steam account")
            : profile.AccountName;
        HomeSteamId.Text = string.IsNullOrWhiteSpace(profile.SteamId64)
            ? ""
            : $"Steam ID {profile.SteamId64}";

        var name = string.IsNullOrWhiteSpace(profile.PersonaName)
            ? profile.AccountName
            : profile.PersonaName;
        HomeSteamAvatarInitials.Text = GetInitials(name);
        HomeSteamAvatarImage.Source = null;
        HomeSteamAvatarImage.Visibility = Visibility.Collapsed;
        HomeSteamAvatarInitials.Visibility = Visibility.Visible;

        if (AccountProfileCache.TryReadImage(AccountProfileCache.SteamPngPath, out var cachedAvatar))
            SetHomeAvatarImage(HomeSteamAvatarImage, HomeSteamAvatarInitials, cachedAvatar);

        _ = LoadSteamHomeAvatarAsync(profile.SteamId64);
    }

    private static string GetInitials(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "S";
        var parts = value.Trim().Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
            return parts[0].Substring(0, Math.Min(2, parts[0].Length)).ToUpperInvariant();
        return string.Concat(parts.Take(2).Select(p => p[0])).ToUpperInvariant();
    }

    private static SteamHomeProfile? TryReadLocalSteamProfile()
    {
        try
        {
            var steamRoots = new List<string>();

            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
            {
                var path = key?.GetValue("SteamPath")?.ToString()
                    ?? key?.GetValue("InstallPath")?.ToString();
                if (!string.IsNullOrWhiteSpace(path)) steamRoots.Add(path);
            }

            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam"))
            {
                var path = key?.GetValue("InstallPath")?.ToString();
                if (!string.IsNullOrWhiteSpace(path)) steamRoots.Add(path);
            }

            steamRoots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
            steamRoots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"));

            foreach (var root in steamRoots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(root)) continue;
                var loginUsers = Path.Combine(root, "config", "loginusers.vdf");
                if (!File.Exists(loginUsers)) continue;

                var text = File.ReadAllText(loginUsers);
                var matches = Regex.Matches(
                    text,
                    @"""(?<id>\d{17})""\s*\{(?<body>.*?)\}",
                    RegexOptions.Singleline | RegexOptions.IgnoreCase);

                Match? selected = null;
                foreach (Match match in matches)
                {
                    if (Regex.IsMatch(match.Groups["body"].Value, @"""MostRecent""\s*""1""", RegexOptions.IgnoreCase))
                    {
                        selected = match;
                        break;
                    }
                    selected ??= match;
                }

                if (selected == null) continue;

                var body = selected.Groups["body"].Value;
                return new SteamHomeProfile
                {
                    SteamId64 = selected.Groups["id"].Value,
                    AccountName = ExtractVdfValue(body, "AccountName"),
                    PersonaName = ExtractVdfValue(body, "PersonaName")
                };
            }
        }
        catch
        {
            // Steam may be installed without a readable loginusers.vdf.
        }

        return null;
    }

    private static string ExtractVdfValue(string body, string key)
    {
        var match = Regex.Match(
            body,
            $@"""{Regex.Escape(key)}""\s*""(?<value>(?:\\.|[^""\\])*)""",
            RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["value"].Value.Replace(@"\/", "/") : "";
    }

    private void SteamHomeAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;

        var profile = TryReadLocalSteamProfile();
        var steamId = profile?.SteamId64 ?? "";
        var baseUrl = $"https://steamcommunity.com/app/{RetroRewindSteamAppId}";

        switch (button.Tag?.ToString())
        {
            case "achievements":
                OpenSteamHomeOverlay("achievements");
                break;
            case "games":
                OpenSteamHomeOverlay("games");
                break;
            case "guides":
                OpenUrl($"{baseUrl}/guides/");
                break;
            case "discussions":
                OpenUrl($"{baseUrl}/discussions/");
                break;
            case "community":
                OpenUrl(baseUrl);
                break;
            case "play":
                LaunchGame_Click(sender, e);
                break;
        }
    }

    

private sealed record SteamHomeGame(uint AppId, string Name, int PlaytimeMinutes, int LastPlayedUnix, string IconHash);
private sealed record SteamHomeAchievement(string ApiName, string Name, string Description, bool Achieved, uint UnlockTime, string IconUrl, string IconGrayUrl);

private async Task RefreshSteamHomeAchievementsAsync()
{
    var profile = TryReadLocalSteamProfile();
    if (profile == null || string.IsNullOrWhiteSpace(profile.SteamId64))
    {
        HomeSteamAchievementSummary.Text = L("Steam profile not detected.");
        HomeSteamAchievementsPanel.Children.Clear();
        return;
    }
    if (string.IsNullOrWhiteSpace(_steamApiKey))
    {
        HomeSteamAchievementSummary.Text = L("Add a Steam Web API key in Settings → Steam.");
        HomeSteamAchievementsPanel.Children.Clear();
        return;
    }

    try
    {
        var schema = await SteamGetJsonAsync(
            "ISteamUserStats/GetSchemaForGame/v2/",
            new Dictionary<string,string>{{"appid", RetroRewindSteamAppId.ToString()}, {"l","english"}});
        var player = await SteamGetJsonAsync(
            "ISteamUserStats/GetPlayerAchievements/v1/",
            new Dictionary<string,string>{{"steamid", profile.SteamId64}, {"appid", RetroRewindSteamAppId.ToString()}, {"l","english"}});

        var schemaAchievements = new Dictionary<string,(string Name,string Desc,string Icon,string Gray)>(StringComparer.OrdinalIgnoreCase);
        if (schema.TryGetProperty("game", out var game) &&
            game.TryGetProperty("availableGameStats", out var stats) &&
            stats.TryGetProperty("achievements", out var achs))
        {
            foreach (var a in achs.EnumerateArray())
            {
                var api = GetJsonString(a, "name");
                if (string.IsNullOrWhiteSpace(api)) continue;
                schemaAchievements[api] = (
                    GetJsonString(a, "displayName"),
                    GetJsonString(a, "description"),
                    GetJsonString(a, "icon"),
                    GetJsonString(a, "icongray"));
            }
        }

        _steamHomeAchievements = new List<SteamHomeAchievement>();
        if (player.TryGetProperty("playerstats", out var ps) &&
            ps.TryGetProperty("achievements", out var pa))
        {
            foreach (var a in pa.EnumerateArray())
            {
                var api = GetJsonString(a, "apiname", "name");
                schemaAchievements.TryGetValue(api, out var info);
                var achieved = a.TryGetProperty("achieved", out var av) && av.ValueKind == JsonValueKind.Number && av.GetInt32() != 0;
                var unlock = a.TryGetProperty("unlocktime", out var uv) && uv.ValueKind == JsonValueKind.Number ? uv.GetUInt32() : 0;
                _steamHomeAchievements.Add(new SteamHomeAchievement(
                    api, string.IsNullOrWhiteSpace(info.Name) ? api : info.Name,
                    info.Desc, achieved, unlock, info.Icon, info.Gray));
            }
        }

        RenderSteamHomeAchievements();
    }
    catch (Exception ex)
    {
        HomeSteamAchievementSummary.Text = L("Could not load achievements.");
        HomeSteamAchievementsPanel.Children.Clear();
        CrashLogger.Write("SteamAchievements", ex);
    }
}

private void RenderSteamHomeAchievements()
{
    HomeSteamAchievementsPanel.Children.Clear();
    var total = _steamHomeAchievements.Count;
    var unlocked = _steamHomeAchievements.Count(x => x.Achieved);
    HomeSteamAchievementSummary.Text = total == 0
        ? L("No achievements returned.")
        : L("{0} / {1} unlocked ({2:0}%)", unlocked, total, unlocked * 100.0 / total);
    HomeSteamAchievementProgress.Value = total == 0 ? 0 : unlocked * 100.0 / total;

    foreach (var a in _steamHomeAchievements.OrderByDescending(x => x.Achieved).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 4), MinHeight = 64 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var icon = new Image { Width = 52, Height = 52, Stretch = Stretch.Uniform };
        var url = a.Achieved ? a.IconUrl : a.IconGrayUrl;
        if (!string.IsNullOrWhiteSpace(url))
            _ = LoadSteamImageAsync(icon, url);
        Grid.SetColumn(icon, 0);
        row.Children.Add(icon);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = a.Name, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(a.Description))
            text.Children.Add(new TextBlock { Text = a.Description, Foreground = (Brush)Resources["SecondaryBrush"], FontSize = 12, TextWrapping = TextWrapping.Wrap });
        var status = a.Achieved
            ? (a.UnlockTime > 0 ? DateTimeOffset.FromUnixTimeSeconds(a.UnlockTime).ToLocalTime().ToString("dd MMM yyyy") : L("Unlocked"))
            : L("Locked");
        text.Children.Add(new TextBlock { Text = status, Foreground = (Brush)Resources["SecondaryBrush"], FontSize = 11, Margin = new Thickness(0,2,0,0) });
        Grid.SetColumn(text, 2);
        row.Children.Add(text);

        HomeSteamAchievementsPanel.Children.Add(row);
    }
}

private async Task RefreshSteamHomeGamesAsync()
{
    var profile = TryReadLocalSteamProfile();
    if (profile == null || string.IsNullOrWhiteSpace(profile.SteamId64))
    {
        HomeSteamGamesPanel.Children.Clear();
        HomeSteamGamesPanel.Children.Add(new TextBlock { Text = L("Steam profile not detected."), Foreground = (Brush)Resources["SecondaryBrush"] });
        return;
    }
    if (string.IsNullOrWhiteSpace(_steamApiKey))
    {
        HomeSteamGamesPanel.Children.Clear();
        HomeSteamGamesPanel.Children.Add(new TextBlock { Text = L("Add a Steam Web API key in Settings → Steam."), Foreground = (Brush)Resources["SecondaryBrush"] });
        return;
    }

    try
    {
        var root = await SteamGetJsonAsync(
            "IPlayerService/GetOwnedGames/v1/",
            new Dictionary<string,string>{
                {"steamid", profile.SteamId64},
                {"include_appinfo","true"},
                {"include_played_free_games","true"},
                {"include_free_sub","true"},
                {"language","english"}});
        _steamHomeGames = new List<SteamHomeGame>();
        if (root.TryGetProperty("response", out var response) &&
            response.TryGetProperty("games", out var games))
        {
            foreach (var g in games.EnumerateArray())
            {
                _steamHomeGames.Add(new SteamHomeGame(
                    g.TryGetProperty("appid", out var id) ? id.GetUInt32() : 0,
                    GetJsonString(g,"name"),
                    g.TryGetProperty("playtime_forever", out var pt) ? pt.GetInt32() : 0,
                    g.TryGetProperty("rtime_last_played", out var rt) ? rt.GetInt32() : 0,
                    GetJsonString(g,"img_icon_url")));
            }
        }
        RenderSteamHomeGames();
    }
    catch (Exception ex)
    {
        HomeSteamGamesPanel.Children.Clear();
        HomeSteamGamesPanel.Children.Add(new TextBlock { Text = L("Could not load Steam games."), Foreground = (Brush)Resources["SecondaryBrush"] });
        CrashLogger.Write("SteamGames", ex);
    }
}

private void RenderSteamHomeGames()
{
    HomeSteamGamesPanel.Children.Clear();
    var query = HomeSteamGamesSearch?.Text?.Trim() ?? "";
    var games = _steamHomeGames
        .Where(g => string.IsNullOrWhiteSpace(query) || g.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
        .OrderByDescending(g => g.PlaytimeMinutes)
        .ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase);

    foreach (var g in games)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 4), MinHeight = 58 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new Image { Width = 64, Height = 32, Stretch = Stretch.Uniform };
        if (g.AppId > 0)
            _ = LoadSteamImageAsync(icon, $"https://cdn.cloudflare.steamstatic.com/steam/apps/{g.AppId}/capsule_231x87.jpg");
        Grid.SetColumn(icon,0); row.Children.Add(icon);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = g.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        var playtime = TimeSpan.FromMinutes(g.PlaytimeMinutes);
        var last = g.LastPlayedUnix > 0 ? DateTimeOffset.FromUnixTimeSeconds(g.LastPlayedUnix).ToLocalTime().ToString("dd MMM yyyy") : "—";
        text.Children.Add(new TextBlock { Text = L("{0:0.0} hrs • Last played {1}", playtime.TotalHours, last), Foreground = (Brush)Resources["SecondaryBrush"], FontSize = 12 });
        Grid.SetColumn(text,2); row.Children.Add(text);

        var play = new Button { Content = L("Play"), Tag = g.AppId, Height = 30, Padding = new Thickness(10,4,10,4), Style = (Style)Resources["SettingsButtonStyle"] };
        play.Click += (_,_) => LaunchSteamApp(g.AppId);
        Grid.SetColumn(play,3); row.Children.Add(play);
        HomeSteamGamesPanel.Children.Add(row);
    }
}

private async Task LoadSteamHomeAvatarAsync(string steamId)
{
    if (string.IsNullOrWhiteSpace(steamId) || string.IsNullOrWhiteSpace(_steamApiKey))
        return;

    try
    {
        var response = await SteamGetJsonAsync(
            "ISteamUser/GetPlayerSummaries/v2/",
            new Dictionary<string, string> { ["steamids"] = steamId });

        if (!response.TryGetProperty("response", out var root) ||
            !root.TryGetProperty("players", out var players) ||
            players.ValueKind != JsonValueKind.Array ||
            players.GetArrayLength() == 0)
            return;

        var player = players[0];
        var accountName = player.TryGetProperty("personaname", out var persona) ? persona.GetString() ?? "" : "";
        var profileUrl = player.TryGetProperty("profileurl", out var profile) ? profile.GetString() ?? "" : "";
        var avatarUrl = player.TryGetProperty("avatarfull", out var avatar) ? avatar.GetString() ?? "" : "";
        var returnedSteamId = player.TryGetProperty("steamid", out var id) ? id.GetString() ?? steamId : steamId;

        var cache = new AccountProfileCache.SteamAccountCache
        {
            SteamId64 = returnedSteamId,
            AccountName = TryReadLocalSteamProfile()?.AccountName ?? "",
            PersonaName = accountName,
            ProfileUrl = profileUrl,
            AvatarUrl = avatarUrl,
            LastUpdatedUtc = DateTime.UtcNow
        };
        AccountProfileCache.SaveSteam(cache);

        if (string.IsNullOrWhiteSpace(avatarUrl)) return;

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RetroRewindModHub/1.0.0");
        var bytes = await client.GetByteArrayAsync(avatarUrl);
        AccountProfileCache.SaveImage(AccountProfileCache.SteamPngPath, bytes, avatarUrl);

        await Dispatcher.InvokeAsync(() => SetHomeAvatarImage(HomeSteamAvatarImage, HomeSteamAvatarInitials, bytes));
    }
    catch (Exception ex)
    {
        // Cached account/profile data remains the offline fallback.
        CrashLogger.Write("SteamHomeAvatar", ex);
    }
}

private static void SetHomeAvatarImage(Image image, TextBlock initials, byte[] bytes)
{
    try
    {
        using var ms = new MemoryStream(bytes);
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = ms;
        bmp.EndInit();
        bmp.Freeze();
        image.Source = bmp;
        image.Stretch = Stretch.UniformToFill;

        // Clip the rendered Image itself to a true circle.  The clip is
        // calculated from the actual control size so both Steam and Nexus
        // avatars remain circular regardless of their frame dimensions.
        image.SizeChanged -= HomeAvatarImage_SizeChanged;
        image.SizeChanged += HomeAvatarImage_SizeChanged;
        ApplyHomeAvatarCircleClip(image);

        image.Visibility = Visibility.Visible;
        initials.Visibility = Visibility.Collapsed;
    }
    catch
    {
        image.Source = null;
        image.Clip = null;
        image.SizeChanged -= HomeAvatarImage_SizeChanged;
        image.Visibility = Visibility.Collapsed;
        initials.Visibility = Visibility.Visible;
    }
}

private static void HomeAvatarImage_SizeChanged(object sender, SizeChangedEventArgs e)
{
    if (sender is Image image)
        ApplyHomeAvatarCircleClip(image);
}

private static void ApplyHomeAvatarCircleClip(Image image)
{
    var width = image.ActualWidth;
    var height = image.ActualHeight;
    if (width <= 0 || height <= 0) return;

    var diameter = Math.Min(width, height);
    image.Clip = new EllipseGeometry(
        new Point(width / 2.0, height / 2.0),
        diameter / 2.0,
        diameter / 2.0);
}

private async Task<JsonElement> SteamGetJsonAsync(string endpoint, Dictionary<string,string> parameters)
{
    parameters["key"] = _steamApiKey;
    var query = string.Join("&", parameters.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    client.DefaultRequestHeaders.UserAgent.ParseAdd("RetroRewindModHub/1.0.0");
    var url = $"https://api.steampowered.com/{endpoint}?{query}";
    using var response = await client.GetAsync(url);
    if (!response.IsSuccessStatusCode)
        throw new InvalidOperationException($"Steam API returned HTTP {(int)response.StatusCode}.");
    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    return doc.RootElement.Clone();
}

private async Task LoadSteamImageAsync(Image image, string url)
{
    try
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var bytes = await client.GetByteArrayAsync(url);
        await Dispatcher.InvokeAsync(() =>
        {
            using var ms = new MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            image.Source = bmp;
        });
    }
    catch { }
}

private void LaunchSteamApp(uint appId)
{
    try { Process.Start(new ProcessStartInfo($"steam://run/{appId}") { UseShellExecute = true }); }
    catch { }
}

private void OpenSteamHomeOverlay(string view)
{
    HomeSteamOverlay.Visibility = Visibility.Visible;
    bool achievements = string.Equals(view, "achievements", StringComparison.OrdinalIgnoreCase);
    HomeSteamAchievementsView.Visibility = achievements ? Visibility.Visible : Visibility.Collapsed;
    HomeSteamGamesView.Visibility = achievements ? Visibility.Collapsed : Visibility.Visible;
    HomeSteamOverlayTitle.Text = achievements ? L("Steam Achievements") : L("Steam Games");
    if (achievements) _ = RefreshSteamHomeAchievementsAsync();
    else _ = RefreshSteamHomeGamesAsync();
}

private void CloseSteamHomeOverlay_Click(object sender, RoutedEventArgs e) => HomeSteamOverlay.Visibility = Visibility.Collapsed;

private void SteamHomeOverlayTab_Click(object sender, RoutedEventArgs e)
{
    if (sender is Button b) OpenSteamHomeOverlay(b.Tag?.ToString() == "achievements" ? "achievements" : "games");
}

private void RefreshSteamGames_Click(object sender, RoutedEventArgs e) => _ = RefreshSteamHomeGamesAsync();
private void HomeSteamGamesSearch_TextChanged(object sender, TextChangedEventArgs e)
{
    if (HomeSteamGamesPanel != null && _steamHomeGames.Count > 0) RenderSteamHomeGames();
}

private async Task RefreshNexusHomeAccountAsync(bool force = false)
{
    if (_nexusHomeRefreshInProgress) return;
    if (!force && DateTime.UtcNow - _nexusHomeAccountCheckedUtc < TimeSpan.FromMinutes(5))
    {
        ApplyNexusHomeAccountUi();
        return;
    }

    // Always load the last known public account snapshot first. This makes the
    // home page useful without an internet connection. A successful request
    // below replaces it with fresh data.
    LoadCachedNexusHomeAccount();

    _nexusHomeRefreshInProgress = true;
    try
    {
        var apiKey = string.IsNullOrWhiteSpace(_nexusApiKey)
            ? NexusSecretStore.Load()
            : _nexusApiKey;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            ApplyNexusHomeAccountUi();
            return;
        }

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RetroRewindModHub/1.0.0");
        client.DefaultRequestHeaders.TryAddWithoutValidation("apikey", apiKey);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");

        using var response = await client.GetAsync("https://api.nexusmods.com/v1/users/validate.json");
        if (!response.IsSuccessStatusCode)
        {
            ApplyNexusHomeAccountUi();
            return;
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        _nexusHomeUserId = GetJsonString(root, "user_id", "member_id", "userid");
        _nexusHomeUserName = GetJsonString(root, "name", "username", "user_name");
        _nexusHomeAvatarUrl = BuildNexusAvatarUrl(_nexusHomeUserId);
        var premium = GetJsonBool(root, "is_premium");
        var supporter = GetJsonBool(root, "is_supporter");

        _nexusHomeAccountType = premium
            ? "Premium"
            : supporter
                ? "Supporter"
                : "Free";

        _nexusHomeDailyRemaining = GetHeaderInt(response, "X-RL-Daily-Remaining");
        _nexusHomeDailyLimit = GetHeaderInt(response, "X-RL-Daily-Limit");
        _nexusHomeHourlyRemaining = GetHeaderInt(response, "X-RL-Hourly-Remaining");
        _nexusHomeHourlyLimit = GetHeaderInt(response, "X-RL-Hourly-Limit");
        _nexusHomeAccountCheckedUtc = DateTime.UtcNow;

        AccountProfileCache.SaveNexus(new AccountProfileCache.NexusAccountCache
        {
            UserId = _nexusHomeUserId,
            UserName = _nexusHomeUserName,
            AccountType = _nexusHomeAccountType,
            ProfileUrl = string.IsNullOrWhiteSpace(_nexusHomeUserId)
                ? ""
                : $"https://www.nexusmods.com/users/{Uri.EscapeDataString(_nexusHomeUserId)}",
            AvatarUrl = _nexusHomeAvatarUrl,
            LastUpdatedUtc = DateTime.UtcNow
        });

        if (!string.IsNullOrWhiteSpace(_nexusHomeAvatarUrl))
        {
            try
            {
                using var imageClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                imageClient.DefaultRequestHeaders.UserAgent.ParseAdd("RetroRewindModHub/1.0.0");
                var bytes = await imageClient.GetByteArrayAsync(_nexusHomeAvatarUrl);
                AccountProfileCache.SaveImage(AccountProfileCache.NexusPngPath, bytes, _nexusHomeAvatarUrl);
                await Dispatcher.InvokeAsync(() => SetHomeAvatarImage(HomeNexusAvatarImage, HomeNexusAvatarInitials, bytes));
            }
            catch (Exception ex)
            {
                CrashLogger.Write("NexusHomeAvatar", ex);
            }
        }

        ApplyNexusHomeAccountUi();
    }
    catch (Exception ex)
    {
        ApplyNexusHomeAccountUi();
        CrashLogger.Write("NexusHomeAccount", ex);
    }
    finally
    {
        _nexusHomeRefreshInProgress = false;
    }
}

private void LoadCachedNexusHomeAccount()
{
    var cached = AccountProfileCache.LoadNexus();
    if (cached == null) return;

    _nexusHomeUserId = cached.UserId;
    _nexusHomeUserName = cached.UserName;
    _nexusHomeAccountType = cached.AccountType;
    _nexusHomeAvatarUrl = BuildNexusAvatarUrl(_nexusHomeUserId);
    _nexusHomeDailyRemaining = -1;
    _nexusHomeDailyLimit = -1;
    _nexusHomeHourlyRemaining = -1;
    _nexusHomeHourlyLimit = -1;
    _nexusHomeAccountCheckedUtc = cached.LastUpdatedUtc;

    if (AccountProfileCache.TryReadImage(AccountProfileCache.NexusPngPath, out var bytes))
        SetHomeAvatarImage(HomeNexusAvatarImage, HomeNexusAvatarInitials, bytes);
}

private static string BuildNexusAvatarUrl(string userId)
{
    if (string.IsNullOrWhiteSpace(userId)) return "";
    return $"https://avatars.nexusmods.com/{Uri.EscapeDataString(userId)}/100";
}

private static string GetJsonString(JsonElement root, params string[] names)
{
    foreach (var name in names)
    {
        if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }
        else if (root.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Number)
        {
            return value.ToString();
        }
    }
    return "";
}

private static bool GetJsonBool(JsonElement root, string name)
{
    return root.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.True;
}

private static int GetHeaderInt(HttpResponseMessage response, string name)
{
    if (!response.Headers.TryGetValues(name, out var values)) return -1;
    return int.TryParse(values.FirstOrDefault(), out var result) ? result : -1;
}

private void ApplyNexusHomeAccountUi()
{
    var name = string.IsNullOrWhiteSpace(_nexusHomeUserName) ? "N" : _nexusHomeUserName;
    HomeNexusAvatarInitials.Text = GetInitials(name);
    HomeNexusHeading.Text = string.IsNullOrWhiteSpace(_nexusHomeUserName) ? L("Nexus Account") : _nexusHomeUserName;

    if (string.IsNullOrWhiteSpace(_nexusHomeUserId) &&
        string.IsNullOrWhiteSpace(_nexusHomeUserName))
    {
        ClearNexusHomeAccountUi(L("Not connected"));
        return;
    }

    HomeNexusUserId.Text = string.IsNullOrWhiteSpace(_nexusHomeUserId)
        ? "—" : _nexusHomeUserId;
    HomeNexusAccount.Text = string.IsNullOrWhiteSpace(_nexusHomeAccountType)
        ? "—" : _nexusHomeAccountType;

    HomeNexusDaily.Text = FormatNexusLimit(_nexusHomeDailyRemaining, _nexusHomeDailyLimit);
    HomeNexusHourly.Text = FormatNexusLimit(_nexusHomeHourlyRemaining, _nexusHomeHourlyLimit);

    // Keep the encrypted account JSON present whenever we have a Nexus account.
    // This is independent of the avatar cache.
    try
    {
        AccountProfileCache.SaveNexus(new AccountProfileCache.NexusAccountCache
        {
            UserId = _nexusHomeUserId,
            UserName = _nexusHomeUserName,
            AccountType = _nexusHomeAccountType,
            ProfileUrl = string.IsNullOrWhiteSpace(_nexusHomeUserId)
                ? ""
                : $"https://www.nexusmods.com/users/{Uri.EscapeDataString(_nexusHomeUserId)}",
            AvatarUrl = _nexusHomeAvatarUrl,
            LastUpdatedUtc = _nexusHomeAccountCheckedUtc == default ? DateTime.UtcNow : _nexusHomeAccountCheckedUtc
        });
    }
    catch (Exception ex) { CrashLogger.Write("NexusAccountJsonRepair", ex); }
}

private static string FormatNexusLimit(int remaining, int limit)
{
    if (remaining < 0 && limit < 0) return "—";
    if (limit < 0) return remaining.ToString();
    if (remaining < 0) return $"—/{limit}";
    return $"{remaining}/{limit}";
}

private void ClearNexusHomeAccountUi(string status)
{
    _nexusHomeUserId = "";
    _nexusHomeUserName = "";
    _nexusHomeAccountType = "";
    _nexusHomeAvatarUrl = "";
    _nexusHomeDailyRemaining = -1;
    _nexusHomeDailyLimit = -1;
    _nexusHomeHourlyRemaining = -1;
    _nexusHomeHourlyLimit = -1;

    HomeNexusUserId.Text = "—";
    HomeNexusAccount.Text = "—";
    HomeNexusDaily.Text = "—";
    HomeNexusHourly.Text = "—";
    HomeNexusAvatarImage.Source = null;
    HomeNexusAvatarImage.Visibility = Visibility.Collapsed;
    HomeNexusAvatarInitials.Visibility = Visibility.Visible;
}

    private async Task RefreshHomeNewsAsync(bool force = false)
    {
        if (_homeNewsLoading) return;
        if (_gameActive) return;
        if (!force && (DateTime.UtcNow - _homeNewsLastRefreshUtc) < HomeNewsRefreshInterval && HomeNewsPanel.Children.Count > 0) return;
        _homeNewsLoading = true;
        try { _homeNewsCts?.Cancel(); } catch { }
        var homeCts = new CancellationTokenSource();
        _homeNewsCts = homeCts;
        HomeNewsPanel.Children.Clear();
        HomeNewsPanel.Children.Add(new TextBlock
        {
            Text = L("Loading Steam updates…"),
            Foreground = (Brush)Resources["SecondaryBrush"],
            Margin = new Thickness(0, 0, 0, 4)
        });

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("RetroRewindModhub/1.0");
            var rss = await client.GetStringAsync("https://steamcommunity.com/games/3552140/rss", homeCts.Token);
            var doc = XDocument.Parse(rss);
            var items = doc.Descendants("item")
                .Take(12)
                .Select(x => new SteamNewsItem(
                    (string?)x.Element("title") ?? L("Steam Update"),
                    (string?)x.Element("pubDate") ?? "",
                    StripHtml((string?)x.Element("description") ?? ""),
                    (string?)x.Element("link") ?? "https://steamcommunity.com/app/3552140/"))
                .ToList();

            _homeNewsLastRefreshUtc = DateTime.UtcNow;
            HomeNewsPanel.Children.Clear();
            if (items.Count == 0)
            {
                HomeNewsPanel.Children.Add(new TextBlock
                {
                    Text = L("No Steam updates were found."),
                    Foreground = (Brush)Resources["SecondaryBrush"]
                });
            }
            else
            {
                foreach (var item in items)
                    AddHomeNewsItem(item);
            }
        }
        catch (OperationCanceledException) when (homeCts.IsCancellationRequested)
        {
            // Gameplay started or the window is closing. Leave the current UI alone.
        }
        catch (Exception ex)
        {
            HomeNewsPanel.Children.Clear();
            HomeNewsPanel.Children.Add(new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = L("Steam updates could not be loaded."), FontWeight = FontWeights.SemiBold },
                    new TextBlock { Text = ex.Message, Margin = new Thickness(0, 4, 0, 0), Foreground = (Brush)Resources["SecondaryBrush"], TextWrapping = TextWrapping.Wrap }
                }
            });
        }
        finally
        {
            _homeNewsLoading = false;
        }
    }

    private void AddHomeNewsItem(SteamNewsItem item)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = item.Title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap
        });
        stack.Children.Add(new TextBlock
        {
            Text = item.Date,
            Margin = new Thickness(0, 1, 0, 4),
            Foreground = (Brush)Resources["SecondaryBrush"],
            FontSize = 12
        });
        stack.Children.Add(new TextBlock
        {
            Text = item.Description,
            TextWrapping = TextWrapping.Wrap
        });

        var postButton = new Button
        {
            Content = stack,
            Style = (Style)Resources["HomeNewsButtonStyle"],
            Margin = new Thickness(0, 0, 0, 10),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        postButton.Click += (_, _) => OpenUrl(item.Url);
        HomeNewsPanel.Children.Add(postButton);
    }

    private static void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch { }
    }

    private static string StripHtml(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var decoded = System.Net.WebUtility.HtmlDecode(text);
        return Regex.Replace(decoded, "<.*?>", " ").Replace("\r", " ").Replace("\n", " ").Trim();
    }

    private static string StripNexusMarkup(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        var decoded = System.Net.WebUtility.HtmlDecode(text);
        decoded = Regex.Replace(decoded, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase);
        decoded = Regex.Replace(decoded, "</p\\s*>", "\n\n", RegexOptions.IgnoreCase);
        decoded = Regex.Replace(decoded, "<p(?:\\s+[^>]*)?>", "", RegexOptions.IgnoreCase);

        // Nexus descriptions use BBCode for formatting. Convert structural tags to
        // readable text and remove presentation-only tags so they never appear raw.
        decoded = Regex.Replace(decoded, @"\[(?:/?)(?:left|right|center|justify|b|strong|u|i|em|s|strike|sup|sub|hr|table|/table|tr|/tr|td|/td|th|/th|quote|/quote|spoiler|/spoiler|font(?:=[^\]]*)?|color(?:=[^\]]*)?|size(?:=[^\]]*)?)\]", "", RegexOptions.IgnoreCase);
        decoded = Regex.Replace(decoded, @"\[(?:url|img)(?:=[^\]]*)?\]", "", RegexOptions.IgnoreCase);
        decoded = Regex.Replace(decoded, @"\[/(?:url|img|list|ul|ol)\]", "", RegexOptions.IgnoreCase);
        decoded = Regex.Replace(decoded, @"\[(?:list|ul|ol)(?:=[^\]]*)?\]", "\n", RegexOptions.IgnoreCase);
        decoded = Regex.Replace(decoded, @"\[\*\]", "• ", RegexOptions.IgnoreCase);
        decoded = Regex.Replace(decoded, @"\[/?(?:br|p)\]", "\n", RegexOptions.IgnoreCase);
        decoded = Regex.Replace(decoded, @"\[/?[^\]]+\]", "", RegexOptions.IgnoreCase);
        decoded = Regex.Replace(decoded, "\n{3,}", "\n\n");
        decoded = Regex.Replace(decoded, "[ \t]+", " ");
        return decoded.Replace(" \n", "\n").Trim();
    }

    private void RefreshModConfigurationPanels()
    {
        if (_gameActive) return;
        Ue4ssConfigureList.Items.Clear();
        Ue4ssSettingsPanel.Children.Clear();
        _selectedConfigMod = null;
        _selectedConfigPath = null;
        _selectedConfigType = null;
        _selectedConfigDefinitionPath = null;
        _configFields.Clear();
        _bundleStates.Clear();
        _yamlTables.Clear();
        ConfigureSelectedModText.Text = L("No mod selected");
        RestoreModSettingsButton.IsEnabled = false;

        var root = GetVerifiedGameRoot();
        foreach (var mod in GetUe4ssMods(root).OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var config = FindModConfig(mod.Path);
            if (config == null) continue;
            CaptureModDefault(mod, config.Value.path, config.Value.type);
            ApplyCustomSettingsForMod(mod, config.Value.path, config.Value.type);
            Ue4ssConfigureList.Items.Add(new ListBoxItem
            {
                Content = mod.Name,
                Tag = mod,
                Padding = new Thickness(8),
                Foreground = (Brush)Resources["ForegroundBrush"]
            });
        }
        if (Ue4ssConfigureList.Items.Count == 0)
        {
            Ue4ssConfigureList.Items.Add(new ListBoxItem
            {
                Content = L("No configurable UE4SS mods detected."),
                IsEnabled = false,
                Foreground = (Brush)Resources["SecondaryBrush"]
            });
        }
    }

    private void CaptureModDefault(ModEntry mod, string configPath, string configType)
    {
        CaptureOrUpdateModDefault(mod, configPath, configType);
    }

    // Defaults are captured from the real mod config. Custom contains only
    // user overrides and is never populated merely because a value differs
    // from Defaults at discovery time.
    private void CaptureOrUpdateModDefault(ModEntry mod, string configPath, string configType)
    {
        try
        {
            if (!File.Exists(configPath)) return;
            var data = LoadModDefaults();
            var key = mod.Name;
            var current = configType.Equals("ini", StringComparison.OrdinalIgnoreCase)
                ? ParseIniDefaultValues(File.ReadAllText(configPath))
                : ParseLuaDefaultValues(File.ReadAllText(configPath));

            if (!TryGetModDefault(data, mod, out var record))
            {
                data[key] = new ModDefaultRecord(
                    mod.Path,
                    GetRelativeConfigPath(mod.Path, configPath),
                    configType,
                    new Dictionary<string, string>(current, StringComparer.OrdinalIgnoreCase),
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
                SaveModDefaults(data);
                return;
            }

            var defaults = new Dictionary<string, string>(record.Defaults ?? new(), StringComparer.OrdinalIgnoreCase);
            var custom = new Dictionary<string, string>(record.Custom ?? new(), StringComparer.OrdinalIgnoreCase);
            bool changed = false;

            // A mod update may add settings. Capture only genuinely new keys.
            foreach (var pair in current)
            {
                if (!defaults.ContainsKey(pair.Key))
                {
                    defaults[pair.Key] = pair.Value;
                    changed = true;
                }
            }

            var relative = GetRelativeConfigPath(mod.Path, configPath);
            if (!string.Equals(record.ModPath, mod.Path, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(record.ConfigPath, relative, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(record.ConfigType, configType, StringComparison.OrdinalIgnoreCase))
            {
                changed = true;
            }

            if (changed)
            {
                data[key] = new ModDefaultRecord(mod.Path, relative, configType, defaults, custom);
                SaveModDefaults(data);
            }
        }
        catch { }
    }

    private static string GetRelativeConfigPath(string modPath, string configPath)
    {
        try
        {
            var relative = Path.GetRelativePath(modPath, configPath);
            return "\\" + relative.Replace(Path.DirectorySeparatorChar, '\\').TrimStart('\\');
        }
        catch { return configPath; }
    }

    private static Dictionary<string, string> ParseIniDefaultValues(string content)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string section = "";
        foreach (var raw in content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
            if (line.StartsWith("[") && line.EndsWith("]")) { section = line[1..^1].Trim(); continue; }
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var name = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            values[string.IsNullOrWhiteSpace(section) ? name : $"{section}.{name}"] = value;
        }
        return values;
    }

    private static Dictionary<string, string> ParseLuaDefaultValues(string content)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("--")) continue;
            var match = Regex.Match(line, @"^(?<key>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>[^,]+),?\s*(?:--.*)?$");
            if (!match.Success) continue;
            var value = match.Groups["value"].Value.Trim();
            if (value.StartsWith("{") || value == "return") continue;
            values[match.Groups["key"].Value] = value;
        }
        return values;
    }

    private void SaveModDefaults(Dictionary<string, ModDefaultRecord> data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ModDefaultsFile)!);
        File.WriteAllText(ModDefaultsFile, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void Ue4ssConfigureList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Ue4ssConfigureList.SelectedItem is not ListBoxItem item || item.Tag is not ModEntry mod) return;
        _selectedConfigMod = mod;
        var config = FindModConfig(mod.Path);
        if (config == null) return;
        // FindModConfig may have just claimed an uncontrolled YAML definition.
        // Protect it before the metadata is consumed by the editor.
        RelockModHubControlledFiles();
        _selectedConfigPath = config.Value.path;
        _selectedConfigType = config.Value.type;
        _selectedConfigDefinitionPath = config.Value.definitionPath;
        CaptureOrUpdateModDefault(mod, _selectedConfigPath, _selectedConfigType);
        ApplyCustomSettingsForMod(mod, _selectedConfigPath, _selectedConfigType);
        ConfigureSelectedModText.Text = $"{mod.Name} — {Path.GetFileName(_selectedConfigPath)}";
        BuildConfigEditor(_selectedConfigPath, _selectedConfigType, _selectedConfigDefinitionPath);
        RestoreModSettingsButton.IsEnabled = LoadModDefaults().ContainsKey(mod.Name) || LoadModDefaults().ContainsKey(mod.Path);
    }

    private void BuildConfigEditor(string path, string type, string? definitionPath = null)
    {
        Ue4ssSettingsPanel.Children.Clear();
        _configFields.Clear();
        string content;
        try
        {
            content = File.ReadAllText(path);
            if (type.Equals("lua", StringComparison.OrdinalIgnoreCase) &&
                _selectedConfigMod != null &&
                Path.GetFileName(path).Equals("config.lua", StringComparison.OrdinalIgnoreCase))
            {
                content = EnsureLuaStaffTwoEntries(path, content);
            }
        }
        catch (Exception ex)
        {
            Ue4ssSettingsPanel.Children.Add(new TextBlock { Text = ex.Message, Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap });
            return;
        }
        if (type == "ini") BuildIniEditor(content, definitionPath);
        else BuildLuaEditor(content, definitionPath);
    }


    private string EnsureLuaStaffTwoEntries(string path, string content)
    {
        try
        {
            var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
            int staffStart = -1;
            int staffEnd = -1;
            int rowCount = 0;
            string rowIndent = "        ";

            for (int i = 0; i < lines.Count; i++)
            {
                if (staffStart < 0 && Regex.IsMatch(lines[i].Trim(), @"^staff\s*=\s*\{\s*$", RegexOptions.IgnoreCase))
                {
                    staffStart = i;
                    continue;
                }

                if (staffStart >= 0)
                {
                    if (lines[i].TrimStart().StartsWith("}", StringComparison.Ordinal))
                    {
                        staffEnd = i;
                        break;
                    }

                    if (Regex.IsMatch(lines[i], @"^\s*(?:--\s*)?\{\s*.*\}\s*,?\s*$"))
                    {
                        rowCount++;
                        if (rowCount == 1)
                        {
                            var m = Regex.Match(lines[i], @"^(?<indent>\s*)");
                            if (m.Success) rowIndent = m.Groups["indent"].Value;
                        }
                    }
                }
            }

            if (staffStart < 0 || staffEnd < 0 || rowCount >= 2)
                return content;

            var additions = new List<string>();
            while (rowCount < 2)
            {
                additions.Add($"{rowIndent}-- {{ name = \"\", salary = 0, skillCheckout = 99, skillReturn = 99 }},");
                rowCount++;
            }

            lines.InsertRange(staffEnd, additions);
            var updated = string.Join(Environment.NewLine, lines);
            WithModHubControlledFilesUnlocked(() => File.WriteAllText(path, updated));
            return updated;
        }
        catch
        {
            return content;
        }
    }

    private void BuildYamlEditor(string content)
    {
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        // YAML indentation is significant, but it is not required to use exactly
        // two spaces.  Detect the config-entry indentation from the first
        // "- key:" entry and use that level consistently throughout the block.
        int configEntryIndent = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            var match = Regex.Match(lines[i], @"^(?<indent>\s*)-\s+key:\s*(?<key>[A-Za-z0-9_]+)\s*$");
            if (match.Success)
            {
                configEntryIndent = match.Groups["indent"].Value.Length;
                break;
            }
        }

        if (configEntryIndent < 0)
        {
            AddNoSettingsMessage();
            return;
        }

        var entryPattern = $@"^\s{{{configEntryIndent}}}-\s+key:\s*(?<key>[A-Za-z0-9_]+)\s*$";

        for (int i = 0; i < lines.Length; i++)
        {
            var m = Regex.Match(lines[i], entryPattern);
            if (!m.Success) continue;

            var key = m.Groups["key"].Value;
            int blockEnd = i + 1;
            while (blockEnd < lines.Length && !Regex.IsMatch(lines[blockEnd], entryPattern))
                blockEnd++;

            string? label = null, desc = null, type = null, defaultValue = null;
            int columnsStart = -1, defaultLine = -1;

            for (int j = i + 1; j < blockEnd; j++)
            {
                var lm = Regex.Match(lines[j], @"^\s+label:\s*(.*?)\s*$");
                if (lm.Success) label = Unquote(lm.Groups[1].Value.Trim());

                var dm = Regex.Match(lines[j], @"^\s+description:\s*(.*?)\s*$");
                if (dm.Success) desc = Unquote(dm.Groups[1].Value.Trim());

                var tm = Regex.Match(lines[j], @"^\s+type:\s*([A-Za-z0-9_-]+)\s*$");
                if (tm.Success) type = tm.Groups[1].Value.Trim().ToLowerInvariant();

                var cm = Regex.Match(lines[j], @"^(?<indent>\s+)columns:\s*$");
                if (cm.Success) columnsStart = j;

                var def = Regex.Match(lines[j], @"^\s+default:\s*(.*)$");
                if (def.Success)
                {
                    defaultLine = j;
                    defaultValue = def.Groups[1].Value.Trim();
                }
            }

            if (string.Equals(type, "table", StringComparison.OrdinalIgnoreCase))
            {
                BuildYamlTableEditor(key, label ?? key, desc, lines, columnsStart, defaultLine, blockEnd);
                i = blockEnd - 1;
                continue;
            }

            if (defaultValue == null) continue;
            AddConfigField(key, label ?? key, type ?? InferConfigType(defaultValue), desc, Unquote(defaultValue));
            i = blockEnd - 1;
        }

        if (_configFields.Count == 0 && _yamlTables.Count == 0) AddNoSettingsMessage();
    }

    private void BuildYamlTableEditor(string key, string label, string? description, string[] lines, int columnsStart, int defaultLine, int blockEnd)
    {
        var state = new YamlTableState { Key = key };
        if (columnsStart >= 0)
        {
            for (int i = columnsStart + 1; i < (defaultLine >= 0 ? defaultLine : blockEnd); i++)
            {
                var cm = Regex.Match(lines[i], @"^\s+-\s+key:\s*([A-Za-z0-9_]+)\s*$");
                if (!cm.Success) continue;

                var columnKey = cm.Groups[1].Value;
                string columnLabel = columnKey;
                string columnType = "text";
                var values = new List<string>();

                int colEnd = i + 1;
                while (colEnd < (defaultLine >= 0 ? defaultLine : blockEnd) &&
                       !Regex.IsMatch(lines[colEnd], @"^\s+-\s+key:"))
                    colEnd++;

                for (int j = i + 1; j < colEnd; j++)
                {
                    var lm = Regex.Match(lines[j], @"^\s+label:\s*[""']?(.*?)[""']?\s*$");
                    if (lm.Success) columnLabel = lm.Groups[1].Value.Trim();

                    var tm = Regex.Match(lines[j], @"^\s+type:\s*(\w+)\s*$");
                    if (tm.Success) columnType = tm.Groups[1].Value.Trim().ToLowerInvariant();

                    var vm = Regex.Match(lines[j], @"^\s*-\s*([^\s#]+)\s*$");
                    if (vm.Success && j > i && columnType == "enum")
                        values.Add(Unquote(vm.Groups[1].Value.Trim()));
                }

                state.Columns.Add(new YamlTableColumn
                {
                    Key = columnKey,
                    Label = columnLabel,
                    Type = columnType,
                    Values = values
                });
                i = colEnd - 1;
            }
        }

        if (state.Columns.Count == 0)
        {
            AddConfigField(key, label, "text", description, "");
            return;
        }

        var outer = new StackPanel { Margin = new Thickness(0, 0, 0, 7) };
        outer.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold });
        if (!string.IsNullOrWhiteSpace(description))
            outer.Children.Add(new TextBlock
            {
                Text = description,
                Foreground = (Brush)Resources["SecondaryBrush"],
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 1, 0, 5)
            });

        var header = new Grid { Margin = new Thickness(0, 0, 0, 3) };
        foreach (var column in state.Columns)
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(column.Type == "enum" ? 150 : 100) });
        for (int c = 0; c < state.Columns.Count; c++)
        {
            if (c == 0) header.ColumnDefinitions[c].Width = new GridLength(1, GridUnitType.Star);
            else header.ColumnDefinitions[c].Width = new GridLength(82);
            AddBundleHeaderCell(header, state.Columns[c].Label, c);
        }
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
        AddBundleHeaderCell(header, L("Enabled"), state.Columns.Count);
        outer.Children.Add(header);

        if (defaultLine >= 0)
        {
            for (int i = defaultLine + 1; i < blockEnd; i++)
            {
                var rowMatch = Regex.Match(lines[i], @"^\s*(?<disabled>#\s*)?-\s*\{(?<body>.*)\}\s*,?\s*$");
                if (!rowMatch.Success) continue;

                var values = ParseYamlInlineMap(rowMatch.Groups["body"].Value);
                var rowState = new YamlTableRowState();
                var row = new Grid { Margin = new Thickness(0, 0, 0, 4), MinHeight = 27 };

                // The header defines the table columns, but each WPF Grid also
                // needs its own ColumnDefinitions. Without these, Grid.SetColumn
                // has no effect and every editor is rendered into column 0.
                for (int c = 0; c < state.Columns.Count; c++)
                    row.ColumnDefinitions.Add(new ColumnDefinition
                    {
                        Width = c == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(90)
                    });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });

                for (int c = 0; c < state.Columns.Count; c++)
                {
                    var column = state.Columns[c];
                    FrameworkElement editor;

                    values.TryGetValue(column.Key, out var raw);
                    raw ??= "";

                    if (column.Type == "enum")
                    {
                        var comboValues = column.Values.ToList();
                        var currentValue = Unquote(raw);
                        if (!comboValues.Contains(currentValue, StringComparer.OrdinalIgnoreCase))
                            comboValues.Add(currentValue);

                        var combo = new ComboBox
                        {
                            Style = (Style)Resources["SettingsComboBoxStyle"],
                            MinHeight = 26,
                            HorizontalContentAlignment = HorizontalAlignment.Center,
                            ItemsSource = comboValues,
                            SelectedItem = comboValues.FirstOrDefault(v => string.Equals(v, currentValue, StringComparison.OrdinalIgnoreCase))
                        };
                        editor = combo;
                    }
                    else
                    {
                        editor = new TextBox
                        {
                            Text = Unquote(raw),
                            Style = (Style)Resources["InputStyle"],
                            MinHeight = 26,
                            VerticalContentAlignment = VerticalAlignment.Center,
                            TextAlignment = TextAlignment.Center
                        };
                    }

                    Grid.SetColumn(editor, c);
                    row.Children.Add(editor);
                    rowState.Editors[column.Key] = editor;
                }

                var toggle = new CheckBox
                {
                    IsChecked = !rowMatch.Groups["disabled"].Success,
                    Style = (Style)Resources["TransferToggleStyle"],
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                toggle.ToolTip = toggle.IsChecked == true ? L("Enabled") : L("Disabled");
                toggle.Checked += (_, _) => toggle.ToolTip = L("Enabled");
                toggle.Unchecked += (_, _) => toggle.ToolTip = L("Disabled");
                Grid.SetColumn(toggle, state.Columns.Count);
                row.Children.Add(toggle);
                rowState.Toggle = toggle;

                outer.Children.Add(row);
                state.Rows.Add(rowState);
            }
        }

        if (state.Rows.Count == 0)
            outer.Children.Add(new TextBlock
            {
                Text = L("No table entries configured."),
                Foreground = (Brush)Resources["SecondaryBrush"],
                Margin = new Thickness(0, 4, 0, 8)
            });

        Ue4ssSettingsPanel.Children.Add(outer);
        _yamlTables[key] = state;
    }

    private static Dictionary<string, string> ParseYamlInlineMap(string body)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(body, @"(?<key>[A-Za-z0-9_]+)\s*:\s*(?<value>""[^""]*""|'[^']*'|[^,]+)"))
            result[match.Groups["key"].Value] = match.Groups["value"].Value.Trim();
        return result;
    }

    private sealed class LuaDefinition
    {
        public string Key { get; init; } = "";
        public string Label { get; init; } = "";
        public string? Description { get; init; }
        public string Type { get; init; } = "text";
        public List<YamlTableColumn> Columns { get; init; } = new();
    }

    private static Dictionary<string, LuaDefinition> ParseLuaDefinitions(string? definitionPath)
    {
        var result = new Dictionary<string, LuaDefinition>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(definitionPath) || !File.Exists(definitionPath)) return result;
        if (!definitionPath.EndsWith(".yaml.RRModHub.CONTROLLED", StringComparison.OrdinalIgnoreCase)) return result;
        try
        {
            var lines = File.ReadAllLines(definitionPath);
            int configIndent = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                var m = Regex.Match(lines[i], @"^(?<indent>\s*)-\s+key:\s*(?<key>[A-Za-z0-9_]+)\s*$");
                if (m.Success) { configIndent = m.Groups["indent"].Value.Length; break; }
            }
            if (configIndent < 0) return result;
            var entryPattern = $@"^\s{{{configIndent}}}-\s+key:\s*(?<key>[A-Za-z0-9_]+)\s*$";
            for (int i = 0; i < lines.Length; i++)
            {
                var km = Regex.Match(lines[i], entryPattern);
                if (!km.Success) continue;
                var key = km.Groups["key"].Value;
                int end = i + 1;
                while (end < lines.Length && !Regex.IsMatch(lines[end], entryPattern)) end++;
                string label = key; string? desc = null;
                int columnsStart = -1;
                for (int j = i + 1; j < end; j++)
                {
                    var lm = Regex.Match(lines[j], @"^\s+label:\s*(.*?)\s*$");
                    if (lm.Success) label = Unquote(lm.Groups[1].Value.Trim());
                    var dm = Regex.Match(lines[j], @"^\s+description:\s*(.*?)\s*$");
                    if (dm.Success) desc = Unquote(dm.Groups[1].Value.Trim());
                    if (Regex.IsMatch(lines[j], @"^\s+columns:\s*$")) columnsStart = j;
                }
                var def = new LuaDefinition { Key = key, Label = label, Description = desc, Type = "text" };
                if (columnsStart >= 0)
                {
                    int sectionEnd = end;
                    for (int j = columnsStart + 1; j < sectionEnd; j++)
                    {
                        var cm = Regex.Match(lines[j], @"^\s+-\s+key:\s*([A-Za-z0-9_]+)\s*$");
                        if (!cm.Success) continue;
                        var ck = cm.Groups[1].Value;
                        int colEnd = j + 1;
                        while (colEnd < sectionEnd && !Regex.IsMatch(lines[colEnd], @"^\s+-\s+key:")) colEnd++;
                        string cl = ck;
                        for (int z = j + 1; z < colEnd; z++)
                        {
                            var lm = Regex.Match(lines[z], @"^\s+label:\s*(.*?)\s*$");
                            if (lm.Success) cl = Unquote(lm.Groups[1].Value.Trim());
                        }
                        def.Columns.Add(new YamlTableColumn { Key = ck, Label = cl, Type = "text" });
                        j = colEnd - 1;
                    }
                }
                result[key] = def;
                i = end - 1;
            }
        }
        catch { }
        return result;
    }

    private void BuildIniEditor(string content, string? definitionPath = null)
    {
        var definitions = ParseLuaDefinitions(definitionPath);
        var currentSection = "";
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith(";") || trimmed.StartsWith("#")) continue;

            var section = Regex.Match(trimmed, @"^\[(?<section>[^\]]+)\]$");
            if (section.Success) { currentSection = section.Groups["section"].Value.Trim(); continue; }

            var m = Regex.Match(line, @"^\s*(?<key>[^=;#\s][^=;]*?)\s*=\s*(?<value>.*?)\s*$");
            if (!m.Success) continue;

            var key = m.Groups["key"].Value.Trim();
            var raw = m.Groups["value"].Value.Trim();
            definitions.TryGetValue(key, out var def);
            var type = InferConfigType(raw);
            AddConfigField(key,
                def?.Label ?? key,
                type,
                def?.Description,
                Unquote(raw));
        }

        if (_configFields.Count == 0) AddNoSettingsMessage();
    }

    private void SaveIniSettings(string path)
    {
        var lines = File.ReadAllLines(path).ToList();
        foreach (var field in _configFields)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                var match = Regex.Match(lines[i], $@"^(?<prefix>\s*{Regex.Escape(field.Key)}\s*=\s*).*?(?<comment>\s*[;#].*)?$", RegexOptions.IgnoreCase);
                if (!match.Success) continue;
                var value = GetEditorValue(field);
                lines[i] = match.Groups["prefix"].Value + value + match.Groups["comment"].Value;
                break;
            }
        }
        WithModHubControlledFilesUnlocked(() => File.WriteAllLines(path, lines));
    }

    private void BuildLuaEditor(string content, string? definitionPath = null)
    {
        var definitions = ParseLuaDefinitions(definitionPath);
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        bool inBundles = false;
        int bundleIndex = 0;
        bool bundleHeadersAdded = false;
        bool inStaff = false;
        var staffState = (YamlTableState?)null;

        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            var trimmed = line.Trim();
            if (Regex.IsMatch(trimmed, @"^bundles\s*=\s*\{", RegexOptions.IgnoreCase)) { inBundles = true; continue; }
            if (inBundles)
            {
                if (trimmed.StartsWith("}", StringComparison.Ordinal)) { inBundles = false; continue; }
                var bundleMatch = Regex.Match(line, @"^\s*(?<disabled>--\s*)?\{\s*genre\s*=\s*(?<genre>[^,]+),\s*size\s*=\s*(?<size>[^,]+),\s*count\s*=\s*(?<count>[^}]+)\}\s*,?\s*$", RegexOptions.IgnoreCase);
                if (!bundleMatch.Success) continue;
                bundleIndex++;
                var genre = Unquote(bundleMatch.Groups["genre"].Value.Trim());
                var size = bundleMatch.Groups["size"].Value.Trim();
                var count = bundleMatch.Groups["count"].Value.Trim();
                if (!bundleHeadersAdded) { AddBundleHeaders(); bundleHeadersAdded = true; }
                AddBundleRow(bundleIndex, genre, size, count, !bundleMatch.Groups["disabled"].Success);
                continue;
            }

            var staffStart = Regex.Match(line, @"^\s*(?<key>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*\{\s*$");
            if (staffStart.Success && staffStart.Groups["key"].Value.Equals("staff", StringComparison.OrdinalIgnoreCase))
            {
                if (definitions.TryGetValue("staff", out var staffDef) && staffDef.Columns.Count > 0)
                {
                    inStaff = true;
                    staffState = new YamlTableState { Key = "staff", IsLua = true };
                    staffState.Columns.AddRange(staffDef.Columns);
                    BuildLuaStaffTableHeader(staffState);
                    continue;
                }
            }
            if (inStaff)
            {
                if (trimmed.StartsWith("}", StringComparison.Ordinal)) { inStaff = false; continue; }
                var rowMatch = Regex.Match(line, @"^\s*(?:--\s*)?\{(?<body>.*)\}\s*,?\s*$");
                if (!rowMatch.Success || staffState == null) continue;
                var values = ParseLuaInlineMap(rowMatch.Groups["body"].Value);
                var disabled = Regex.IsMatch(line, @"^\s*--\s*\{");
                AddLuaStaffRow(staffState, values, lineIndex, !disabled);
                continue;
            }

            var m = Regex.Match(line, @"^\s*(?<key>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>.+?)(,\s*)?(--.*)?$");
            if (!m.Success) continue;
            var key = m.Groups["key"].Value;
            var raw = m.Groups["value"].Value.Trim();
            if (raw.StartsWith("function", StringComparison.OrdinalIgnoreCase) || raw.StartsWith("{", StringComparison.Ordinal)) continue;
            definitions.TryGetValue(key, out var def);
            var type = InferConfigType(raw);
            AddConfigField(key, def?.Label ?? key, type, def?.Description, Unquote(raw));
        }
        if (staffState != null) _yamlTables[staffState.Key] = staffState;
        if (_configFields.Count == 0 && _bundleStates.Count == 0 && _yamlTables.Count == 0) AddNoSettingsMessage();
    }

    private static Dictionary<string, string> ParseLuaInlineMap(string body)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(body, @"(?<key>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>""[^""]*""|'[^']*'|[^,]+)"))
            result[match.Groups["key"].Value] = match.Groups["value"].Value.Trim();
        return result;
    }

    private void BuildLuaTableHeader(YamlTableState state, string label, string? description)
    {
        var outer = new StackPanel { Margin = new Thickness(0, 0, 0, 7) };
        outer.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold });
        if (!string.IsNullOrWhiteSpace(description)) outer.Children.Add(new TextBlock { Text = description, Foreground = (Brush)Resources["SecondaryBrush"], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 5) });
        var header = new Grid { Margin = new Thickness(0, 0, 0, 3) };
        foreach (var c in state.Columns) header.ColumnDefinitions.Add(new ColumnDefinition { Width = c.Type == "str" || c.Type == "text" ? new GridLength(1, GridUnitType.Star) : new GridLength(90) });
        if (header.ColumnDefinitions.Count == 0) return;
        for (int i = 0; i < state.Columns.Count; i++) AddBundleHeaderCell(header, state.Columns[i].Label, i);
        outer.Children.Add(header);
        Ue4ssSettingsPanel.Children.Add(outer);
        state._uiContainer = outer;
    }

    private List<YamlTableColumn> ParseLuaDefinitionsForTable(string key)
    {
        // Re-read the active definition through the current selected config path.
        var config = _selectedConfigMod != null ? FindModConfig(_selectedConfigMod.Path) : null;
        return config?.definitionPath is string p ? ParseLuaDefinitions(p).TryGetValue(key, out var d) ? d.Columns : new List<YamlTableColumn>() : new List<YamlTableColumn>();
    }


    private void BuildLuaStaffTableHeader(YamlTableState state)
    {
        var outer = new StackPanel { Margin = new Thickness(0, 0, 0, 7) };

        // Employee Mod uses a fixed two-slot staff editor.  The YAML supplies
        // the column labels/descriptions; the Lua config remains authoritative.
        outer.Children.Add(new TextBlock
        {
            Text = "Staff",
            FontWeight = FontWeights.SemiBold
        });

        // Use the definition description from the controlled YAML; YAML is
        // presentation metadata only, while the Lua config supplies the values.
        if (_selectedConfigDefinitionPath is string definitionPath)
        {
            var defs = ParseLuaDefinitions(definitionPath);
            if (defs.TryGetValue("staff", out var def) && !string.IsNullOrWhiteSpace(def.Description))
            {
                outer.Children.Add(new TextBlock
                {
                    Text = def.Description,
                    Foreground = (Brush)Resources["SecondaryBrush"],
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 1, 0, 5)
                });
            }
        }

        var header = new Grid { Margin = new Thickness(0, 0, 0, 3) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });

        for (int i = 0; i < state.Columns.Count && i < 4; i++)
            AddBundleHeaderCell(header, state.Columns[i].Label, i);
        AddBundleHeaderCell(header, L("Enabled"), 4);

        outer.Children.Add(header);
        Ue4ssSettingsPanel.Children.Add(outer);
        state._uiContainer = outer;
    }

    private void AddLuaStaffRow(YamlTableState state, Dictionary<string, string> values, int sourceLine, bool enabled)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 4), MinHeight = 27 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });

        var rowState = new YamlTableRowState();
        for (int c = 0; c < state.Columns.Count && c < 4; c++)
        {
            var column = state.Columns[c];
            values.TryGetValue(column.Key, out var raw);
            raw ??= "";

            var editor = new TextBox
            {
                Text = Unquote(raw),
                Style = (Style)Resources["InputStyle"],
                Height = 22,
                MinHeight = 22,
                Padding = new Thickness(6, 1, 6, 1),
                VerticalContentAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(c == 0 ? 0 : 2, 0, 2, 0)
            };
            Grid.SetColumn(editor, c);
            row.Children.Add(editor);
            rowState.Editors[column.Key] = editor;
            AttachAutoSave(editor);
        }

        var toggle = new CheckBox
        {
            IsChecked = enabled,
            Style = (Style)Resources["TransferToggleStyle"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        toggle.ToolTip = enabled ? L("Enabled") : L("Disabled");
        toggle.Checked += (_, _) => toggle.ToolTip = L("Enabled");
        toggle.Unchecked += (_, _) => toggle.ToolTip = L("Disabled");
        AttachAutoSave(toggle);
        Grid.SetColumn(toggle, 4);
        row.Children.Add(toggle);
        rowState.Toggle = toggle;

        state._uiContainer?.Children.Add(row);
        state.Rows.Add(rowState);
        state.SourceLineIndices.Add(sourceLine);
    }

    private void AddLuaTableRow(YamlTableState state, Dictionary<string, string> values, int sourceLine)
    {
        var columns = state.Columns;
        if (columns.Count == 0) return;
        var row = new Grid { Margin = new Thickness(0, 0, 0, 4), MinHeight = 27 };
        foreach (var c in columns) row.ColumnDefinitions.Add(new ColumnDefinition { Width = c.Type is "str" or "text" ? new GridLength(1, GridUnitType.Star) : new GridLength(82) });
        var rowState = new YamlTableRowState();
        foreach (var c in columns)
        {
            values.TryGetValue(c.Key, out var raw); raw ??= "";
            FrameworkElement editor = new TextBox { Text = Unquote(raw), Style = (Style)Resources["InputStyle"], Height = 22, MinHeight = 22, Padding = new Thickness(6, 1, 6, 1), VerticalContentAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
            Grid.SetColumn(editor, rowState.Editors.Count); row.Children.Add(editor); rowState.Editors[c.Key] = editor; AttachAutoSave(editor);
        }
        var parent = _yamlTables.TryGetValue(state.Key, out var existing) && existing._uiContainer != null ? existing._uiContainer : state._uiContainer;
        parent?.Children.Add(row);
        state.Rows.Add(rowState); state.SourceLineIndices.Add(sourceLine);
    }

    private void AddBundleHeaders()
    {
        var header = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
        AddBundleHeaderCell(header, L("Genre"), 0);
        AddBundleHeaderCell(header, L("Size"), 1);
        AddBundleHeaderCell(header, L("Count"), 2);
        AddBundleHeaderCell(header, L("Enabled"), 3);
        Ue4ssSettingsPanel.Children.Add(header);
    }

    private static void AddBundleHeaderCell(Grid grid, string text, int column)
    {
        var tb = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 0, 4, 0) };
        Grid.SetColumn(tb, column);
        grid.Children.Add(tb);
    }

    private void AddBundleRow(int index, string genre, string size, string count, bool enabled)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 4), MinHeight = 27 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });

        var genreText = new TextBlock
        {
            Text = genre,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 8, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(genreText, 0);
        row.Children.Add(genreText);

        var sizeBox = new TextBox { Text = Unquote(size), Style = (Style)Resources["InputStyle"], Height = 22, MinHeight = 22, Padding = new Thickness(6, 1, 6, 1), VerticalContentAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
        Grid.SetColumn(sizeBox, 1);
        row.Children.Add(sizeBox);

        var countBox = new TextBox { Text = Unquote(count), Style = (Style)Resources["InputStyle"], Height = 22, MinHeight = 22, Padding = new Thickness(6, 1, 6, 1), VerticalContentAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
        Grid.SetColumn(countBox, 2);
        row.Children.Add(countBox);

        var toggle = new CheckBox
        {
            IsChecked = enabled,
            Style = (Style)Resources["TransferToggleStyle"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = enabled ? L("Enabled") : L("Disabled")
        };
        toggle.Checked += (_, _) => toggle.ToolTip = L("Enabled");
        toggle.Unchecked += (_, _) => toggle.ToolTip = L("Disabled");
        Grid.SetColumn(toggle, 3);
        row.Children.Add(toggle);

        Ue4ssSettingsPanel.Children.Add(row);
        _configFields.Add(new ConfigField { Key = $"__bundle_{index}_size", Label = L("Size"), Type = InferConfigType(size), Editor = sizeBox });
        AttachAutoSave(sizeBox);
        _configFields.Add(new ConfigField { Key = $"__bundle_{index}_count", Label = L("Count"), Type = InferConfigType(count), Editor = countBox });
        AttachAutoSave(countBox);
        _bundleStates[index] = new BundleState { Index = index, Genre = genre, Enabled = enabled, Toggle = toggle };

        Ue4ssSettingsPanel.Children.Add(new Border
        {
            Height = 1,
            Background = (Brush)Resources["SecondaryBrush"],
            Opacity = 0.35,
            Margin = new Thickness(0, 0, 0, 4)
        });
    }

    private void AddNoSettingsMessage()
    {
        Ue4ssSettingsPanel.Children.Add(new TextBlock { Text = L("No editable settings were detected."), Foreground = (Brush)Resources["SecondaryBrush"], TextWrapping = TextWrapping.Wrap });
    }

    private void AddConfigField(string key, string label, string type, string? description, string value)
    {
        var outer = new StackPanel { Margin = new Thickness(0, 0, 0, 7) };
        outer.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold });
        if (!string.IsNullOrWhiteSpace(description)) outer.Children.Add(new TextBlock { Text = description, Foreground = (Brush)Resources["SecondaryBrush"], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 4) });
        FrameworkElement editor;
        if (type == "bool")
        {
            var box = new CheckBox { IsChecked = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase), Style = (Style)Resources["TransferToggleStyle"] };
            editor = box;
        }
        else
        {
            var box = new TextBox { Text = value, Style = (Style)Resources["InputStyle"], Height = 22, MinHeight = 22, Padding = new Thickness(6, 1, 6, 1), TextAlignment = TextAlignment.Center };
            editor = box;
        }
        outer.Children.Add(editor);
        Ue4ssSettingsPanel.Children.Add(outer);
        _configFields.Add(new ConfigField { Key = key, Label = label, Type = type, Description = description, Editor = editor });
        AttachAutoSave(editor);
    }

    private void AttachAutoSave(FrameworkElement editor)
    {
        if (editor is CheckBox checkBox)
        {
            checkBox.Checked += ConfigEditorChanged;
            checkBox.Unchecked += ConfigEditorChanged;
        }
        else if (editor is ComboBox comboBox)
        {
            comboBox.SelectionChanged += ConfigEditorChanged;
        }
        else if (editor is TextBox textBox)
        {
            textBox.LostFocus += ConfigEditorChanged;
            textBox.KeyDown += ConfigEditorKeyDown;
        }
    }

    private void ConfigEditorKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (sender is TextBox textBox) textBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
        }
    }

    private void ConfigEditorChanged(object? sender, RoutedEventArgs e)
    {
        SaveCurrentConfigSilently();
    }

    private void ConfigEditorChanged(object? sender, SelectionChangedEventArgs e)
    {
        SaveCurrentConfigSilently();
    }

    private void SaveCurrentConfigSilently()
    {
        if (_selectedConfigMod == null || string.IsNullOrWhiteSpace(_selectedConfigPath) || string.IsNullOrWhiteSpace(_selectedConfigType)) return;
        try
        {
            if (_selectedConfigType.Equals("ini", StringComparison.OrdinalIgnoreCase)) SaveIniSettings(_selectedConfigPath);
            else SaveLuaSettings(_selectedConfigPath);
            UpdateCustomSettingsFromLiveConfig();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, L("Could not save settings:\n\n{0}", ex.Message), L("Configure Mods"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateCustomSettingsFromLiveConfig()
    {
        if (_selectedConfigMod == null || string.IsNullOrWhiteSpace(_selectedConfigPath) || string.IsNullOrWhiteSpace(_selectedConfigType)) return;
        try
        {
            var data = LoadModDefaults();
            if (!TryGetModDefault(data, _selectedConfigMod, out var record)) return;
            var current = _selectedConfigType.Equals("ini", StringComparison.OrdinalIgnoreCase)
                ? ParseIniDefaultValues(File.ReadAllText(_selectedConfigPath))
                : ParseLuaDefaultValues(File.ReadAllText(_selectedConfigPath));
            var defaults = record.Defaults ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var custom = new Dictionary<string, string>(record.Custom ?? new(), StringComparer.OrdinalIgnoreCase);
            foreach (var pair in current)
            {
                if (!defaults.TryGetValue(pair.Key, out var defaultValue)) continue;
                if (string.Equals(pair.Value, defaultValue, StringComparison.OrdinalIgnoreCase)) custom.Remove(pair.Key);
                else custom[pair.Key] = pair.Value;
            }
            data[_selectedConfigMod.Name] = record with { Custom = custom };
            SaveModDefaults(data);
        }
        catch { }
    }

    private static string InferConfigType(string value)
    {
        var v = value.Trim().TrimEnd(',');
        if (v.Equals("true", StringComparison.OrdinalIgnoreCase) || v.Equals("false", StringComparison.OrdinalIgnoreCase)) return "bool";
        if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) return "int";
        if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) return "float";
        return "text";
    }

    private static string Unquote(string value)
    {
        var v = value.Trim().TrimEnd(',');
        if (v.Length >= 2 && ((v[0] == '"' && v[^1] == '"') || (v[0] == '\'' && v[^1] == '\''))) return v[1..^1];
        return v;
    }

    private void SaveLuaSettings(string path)
    {
        var lines = File.ReadAllLines(path).ToList();
        var bundleFieldMap = new Dictionary<int, Dictionary<string, ConfigField>>();

        foreach (var field in _configFields)
        {
            var bundleMatch = Regex.Match(field.Key, @"^__bundle_(\d+)_(genre|size|count)$", RegexOptions.IgnoreCase);
            if (bundleMatch.Success)
            {
                int index = int.Parse(bundleMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                if (!bundleFieldMap.TryGetValue(index, out var map))
                {
                    map = new Dictionary<string, ConfigField>(StringComparer.OrdinalIgnoreCase);
                    bundleFieldMap[index] = map;
                }
                map[bundleMatch.Groups[2].Value] = field;
                continue;
            }

            for (int i = 0; i < lines.Count; i++)
            {
                if (!Regex.IsMatch(lines[i], $@"^\s*{Regex.Escape(field.Key)}\s*=")) continue;
                var value = FormatConfigValue(GetEditorValue(field), field.Type);
                lines[i] = Regex.Replace(lines[i], $@"^(\s*{Regex.Escape(field.Key)}\s*=\s*).*$", m => m.Groups[1].Value + value + ",");
                break;
            }
        }

        foreach (var table in _yamlTables.Values.Where(t => t.IsLua && !t.Key.Equals("staff", StringComparison.OrdinalIgnoreCase)))
        {
            var columns = table.Columns;
            for (int r = 0; r < table.Rows.Count && r < table.SourceLineIndices.Count; r++)
            {
                int lineIndex = table.SourceLineIndices[r];
                if (lineIndex < 0 || lineIndex >= lines.Count) continue;
                var original = lines[lineIndex];
                foreach (var column in columns)
                {
                    if (!table.Rows[r].Editors.TryGetValue(column.Key, out var editor)) continue;
                    var value = GetEditorValue(editor);
                    var formatted = FormatConfigValue(value, column.Type);
                    var pattern = $@"(\b{Regex.Escape(column.Key)}\s*=\s*)[^,}}]+";
                    lines[lineIndex] = Regex.Replace(lines[lineIndex], pattern, m => m.Groups[1].Value + formatted);
                }
            }
        }

        // Employee Mod: staff is a fixed two-entry table. Preserve the Lua
        // structure and use "-- " at the beginning of a row to disable it.
        if (_yamlTables.TryGetValue("staff", out var staffTable) && staffTable.IsLua)
        {
            bool inStaff = false;
            int staffIndex = 0;

            for (int i = 0; i < lines.Count; i++)
            {
                var trimmed = lines[i].Trim();

                if (Regex.IsMatch(trimmed, @"^staff\s*=\s*\{", RegexOptions.IgnoreCase))
                {
                    inStaff = true;
                    continue;
                }

                if (!inStaff) continue;

                if (trimmed.StartsWith("}", StringComparison.Ordinal))
                {
                    inStaff = false;
                    continue;
                }

                var rowMatch = Regex.Match(lines[i],
                    @"^(?<indent>\s*)(?<disabled>--\s*)?\{\s*(?<body>.*)\}\s*,?\s*$");
                if (!rowMatch.Success) continue;

                staffIndex++;
                if (staffIndex > 2) continue;
                if (staffIndex > staffTable.Rows.Count) continue;

                var row = staffTable.Rows[staffIndex - 1];
                var name = row.Editors.TryGetValue("name", out var nameEditor)
                    ? FormatConfigValue(GetEditorValue(nameEditor), "text") : "\"\"";
                var salary = row.Editors.TryGetValue("salary", out var salaryEditor)
                    ? FormatConfigValue(GetEditorValue(salaryEditor), "int") : "0";
                var checkout = row.Editors.TryGetValue("skillCheckout", out var checkoutEditor)
                    ? FormatConfigValue(GetEditorValue(checkoutEditor), "int") : "99";
                var ret = row.Editors.TryGetValue("skillReturn", out var returnEditor)
                    ? FormatConfigValue(GetEditorValue(returnEditor), "int") : "99";

                var enabled = row.Toggle?.IsChecked == true;
                var prefix = enabled ? "" : "-- ";
                var indent = rowMatch.Groups["indent"].Value;
                lines[i] = $"{indent}{prefix}{{ name = {name}, salary = {salary}, skillCheckout = {checkout}, skillReturn = {ret} }},";
            }

            // The UI always exposes two slots. If the source had fewer than two
            // entries, append the missing slots before the staff table's closing brace.
            int existingRows = staffIndex;
            if (existingRows < 2)
            {
                int closeIndex = -1;
                bool foundStaff = false;
                for (int i = 0; i < lines.Count; i++)
                {
                    if (Regex.IsMatch(lines[i].Trim(), @"^staff\s*=\s*\{", RegexOptions.IgnoreCase))
                    {
                        foundStaff = true;
                        continue;
                    }
                    if (foundStaff && lines[i].TrimStart().StartsWith("}", StringComparison.Ordinal))
                    {
                        closeIndex = i;
                        break;
                    }
                }

                if (closeIndex >= 0 && staffTable.Rows.Count < 2)
                {
                    var sourceIndent = "        ";
                    if (existingRows > 0)
                    {
                        var firstRowIndex = staffTable.SourceLineIndices.FirstOrDefault(x => x >= 0);
                        if (firstRowIndex >= 0 && firstRowIndex < lines.Count)
                        {
                            var m = Regex.Match(lines[firstRowIndex], @"^(?<indent>\s*)");
                            if (m.Success) sourceIndent = m.Groups["indent"].Value;
                        }
                    }
                    lines.Insert(closeIndex, $"{sourceIndent}-- {{ name = \"\", salary = 0, skillCheckout = 99, skillReturn = 99 }},");
                }
            }
        }

        // Update each bundles entry in-place while preserving the original bundle order.
        if (_bundleStates.Count > 0)
        {
            bool inBundles = false;
            int bundleIndex = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                var trimmed = lines[i].Trim();
                if (Regex.IsMatch(trimmed, @"^bundles\s*=\s*\{", RegexOptions.IgnoreCase))
                {
                    inBundles = true;
                    continue;
                }
                if (!inBundles) continue;
                if (trimmed.StartsWith("}", StringComparison.Ordinal))
                {
                    inBundles = false;
                    continue;
                }

                var match = Regex.Match(lines[i],
                    @"^(?<indent>\s*)(?<disabled>--\s*)?\{\s*genre\s*=\s*(?<genre>[^,]+),\s*size\s*=\s*(?<size>[^,]+),\s*count\s*=\s*(?<count>[^}]+)\}(?<comma>\s*,?\s*)$",
                    RegexOptions.IgnoreCase);
                if (!match.Success) continue;

                bundleIndex++;
                if (!_bundleStates.TryGetValue(bundleIndex, out var state)) continue;

                var sizeField = _configFields.FirstOrDefault(f => f.Key.Equals($"__bundle_{bundleIndex}_size", StringComparison.OrdinalIgnoreCase));
                var countField = _configFields.FirstOrDefault(f => f.Key.Equals($"__bundle_{bundleIndex}_count", StringComparison.OrdinalIgnoreCase));
                var size = sizeField != null ? FormatConfigValue(GetEditorValue(sizeField), sizeField.Type) : match.Groups["size"].Value.Trim();
                var count = countField != null ? FormatConfigValue(GetEditorValue(countField), countField.Type) : match.Groups["count"].Value.Trim();
                var genre = match.Groups["genre"].Value.Trim();
                var comma = match.Groups["comma"].Value.Trim();
                if (string.IsNullOrEmpty(comma)) comma = ",";

                var prefix = state.Toggle.IsChecked == true ? "" : "-- ";
                lines[i] = $"{match.Groups["indent"].Value}{prefix}{{ genre = {genre}, size = {size}, count = {count} }}{comma}";
            }
        }

        WithModHubControlledFilesUnlocked(() => File.WriteAllLines(path, lines));
    }

    private static string GetEditorValue(ConfigField field) => GetEditorValue(field.Editor);

    private static string GetEditorValue(FrameworkElement editor)
    {
        if (editor is CheckBox box) return box.IsChecked == true ? "true" : "false";
        if (editor is ComboBox combo) return combo.SelectedItem?.ToString() ?? "";
        return (editor as TextBox)?.Text?.Trim() ?? "";
    }

    private static string FormatConfigValue(string value, string type)
    {
        if (type == "bool") return value.Equals("true", StringComparison.OrdinalIgnoreCase) ? "true" : "false";
        if (type is "int" or "float") return value;
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    private static bool TryGetModDefault(Dictionary<string, ModDefaultRecord> data, ModEntry mod, out ModDefaultRecord record)
    {
        if (data.TryGetValue(mod.Name, out record!)) return true;
        if (data.TryGetValue(mod.Path, out record!)) return true;
        record = default!;
        return false;
    }

    private void ApplyCustomSettingsForMod(ModEntry mod, string configPath, string configType)
    {
        try
        {
            var data = LoadModDefaults();
            if (!TryGetModDefault(data, mod, out var record) || record.Custom == null || record.Custom.Count == 0) return;
            WithModHubControlledFilesUnlocked(() => RestoreDefaultValues(configPath, configType, record.Custom));
        }
        catch { }
    }

    private static string ResolveStoredConfigPath(string modPath, string storedPath)
    {
        if (Path.IsPathRooted(storedPath)) return storedPath;
        var relative = storedPath.TrimStart('\\', '/').Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(modPath, relative);
    }

    private static void RestoreDefaultValues(string path, string type, Dictionary<string, string> values)
    {
        if (type.Equals("ini", StringComparison.OrdinalIgnoreCase))
        {
            var lines = File.ReadAllLines(path);
            string section = "";
            for (int i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();
                if (trimmed.StartsWith("[") && trimmed.EndsWith("]")) { section = trimmed[1..^1].Trim(); continue; }
                var eq = lines[i].IndexOf('=');
                if (eq <= 0) continue;
                var key = lines[i][..eq].Trim();
                var full = string.IsNullOrWhiteSpace(section) ? key : $"{section}.{key}";
                if (values.TryGetValue(full, out var value)) lines[i] = lines[i][..(eq + 1)] + value;
            }
            File.WriteAllLines(path, lines);
            return;
        }

        var luaLines = File.ReadAllLines(path);
        for (int i = 0; i < luaLines.Length; i++)
        {
            foreach (var pair in values)
            {
                var pattern = $@"^(\s*){Regex.Escape(pair.Key)}(\s*=\s*)[^,]+(,?.*)$";
                if (Regex.IsMatch(luaLines[i], pattern))
                {
                    luaLines[i] = Regex.Replace(luaLines[i], pattern, m => m.Groups[1].Value + pair.Key + m.Groups[2].Value + pair.Value + m.Groups[3].Value);
                    break;
                }
            }
        }
        File.WriteAllLines(path, luaLines);
    }

    private void RestoreModSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedConfigMod == null) return;
        try
        {
            var data = LoadModDefaults();
            if (!TryGetModDefault(data, _selectedConfigMod, out var record)) return;
            var configPath = ResolveStoredConfigPath(_selectedConfigMod.Path, record.ConfigPath);
            if (!File.Exists(configPath)) return;

            WithModHubControlledFilesUnlocked(() => RestoreDefaultValues(configPath, record.ConfigType, record.Defaults ?? new Dictionary<string, string>()));
            data[_selectedConfigMod.Name] = record with { Custom = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) };
            SaveModDefaults(data);
            BuildConfigEditor(configPath, record.ConfigType, FindModConfig(_selectedConfigMod.Path)?.definitionPath);
            MessageBox.Show(this, L("Default settings restored."), L("Configure Mods"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, L("Could not restore settings:\n\n{0}", ex.Message), L("Configure Mods"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Mode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string tag)
        {
            RememberCurrentPaths();

            // Always tear down the current video before changing pages.
            // This also cancels an in-progress LibVLC load so a stale async
            // operation cannot recreate the player after navigation.
            if (_mode == "videoeditor" && !string.Equals(tag, "videoeditor", StringComparison.OrdinalIgnoreCase))
                StopAndReleaseVideoEditorPreview();

            if (tag == "videoeditor" && !AreRequiredVideoEditorFilesAvailable())
            {
                _mode = "requiredfiles";
                UpdateMode();
                RefreshRequiredFilesPage();
                return;
            }
            _mode = tag;
            if (string.Equals(tag, "assets", StringComparison.OrdinalIgnoreCase))
            {
                _assetWorkshopActiveType = "";
            }
            UpdateMode();
            RestoreRememberedPaths();
        }
    }

    private void SaveConfig(Dictionary<string, string?> values)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(values, options);

        try
        {
            var directory = Path.GetDirectoryName(RememberedPathsFile);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(RememberedPathsFile, json);
            return;
        }
        catch
        {
            // Fall through to the per-user application-data location.
        }

        try
        {
            var fallback = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RetroRewind", "RetroRewindModhub.json");
            Directory.CreateDirectory(Path.GetDirectoryName(fallback)!);
            File.WriteAllText(fallback, json);
        }
        catch
        {
            // Configuration must never block normal use.
        }
    }

    private void RestoreSettings()
    {
        var values = LoadConfig();

        var defaultSave = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RetroRewind", "Saved", "SaveGames");

        _saveFolderPath = values.GetValueOrDefault("settings.saveFolder");
        if (string.IsNullOrWhiteSpace(_saveFolderPath))
            _saveFolderPath = defaultSave;

        var configuredModsFolder = values.GetValueOrDefault("settings.modsFolder");
        if (string.IsNullOrWhiteSpace(configuredModsFolder))
        {
            var legacyModhubFolder = values.GetValueOrDefault("settings.modhubFolder");
            configuredModsFolder = string.IsNullOrWhiteSpace(legacyModhubFolder)
                ? Path.Combine(DefaultModhubFolder, "Mods")
                : Path.Combine(legacyModhubFolder, "Mods");
        }
        _modsFolderPath = configuredModsFolder;
        NexusSecretStore.Configure(ModsRoot);
        SteamSecretStore.Configure(ModsRoot);
        try
        {
            Directory.CreateDirectory(ModhubFolderPath);
            Directory.CreateDirectory(BlueprintFolderPath);
            Directory.CreateDirectory(ModsRoot);
        }
        catch { }

        _selectedPalette = values.GetValueOrDefault("settings.palette") ?? "60s Mod";
        if (string.Equals(_selectedPalette, "Core UI", StringComparison.OrdinalIgnoreCase))
            _selectedPalette = "60s Mod";

        _selectedFont = values.GetValueOrDefault("settings.font") ?? SupportedFonts[0];
        _showUe4ssDefaultMods = string.Equals(values.GetValueOrDefault("settings.showUe4ssDefaultMods"), "true", StringComparison.OrdinalIgnoreCase);
        _powerSaveMode = values.GetValueOrDefault("settings.powerSaveMode")?.ToLowerInvariant() ?? "auto";
        if (_powerSaveMode is not ("auto" or "powersaving" or "performance")) _powerSaveMode = "auto";
        _runAsAdmin = string.Equals(values.GetValueOrDefault("settings.runAsAdmin"), "true", StringComparison.OrdinalIgnoreCase);
        _autoStartWithWindowsLogin = string.Equals(values.GetValueOrDefault("settings.autoStartWithWindowsLogin"), "true", StringComparison.OrdinalIgnoreCase);
        _enableWindowsNotifications = string.Equals(values.GetValueOrDefault("settings.enableWindowsNotifications"), "true", StringComparison.OrdinalIgnoreCase);
        _closeToTaskbar = string.Equals(values.GetValueOrDefault("settings.closeToTaskbar"), "true", StringComparison.OrdinalIgnoreCase);

        var savedRunLibraries = values.GetValueOrDefault("settings.runForceLoadLibraries");
        _runForceLoadLibraries = string.IsNullOrWhiteSpace(savedRunLibraries)
            ? new List<string> { "dwmapi.dll" }
            : savedRunLibraries.Split(new[] { '|', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        if (_runForceLoadLibraries.Count == 0)
            _runForceLoadLibraries.Add("dwmapi.dll");

        var savedRunPairs = values.GetValueOrDefault("settings.runPairs");
        _runLaunchExecutables = new List<string>();
        _runForceLoadLibraries = new List<string>();
        if (!string.IsNullOrWhiteSpace(savedRunPairs))
        {
            foreach (var pair in savedRunPairs.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split(new[] { '|' }, 2);
                if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1]))
                {
                    _runLaunchExecutables.Add(parts[0].Trim());
                    _runForceLoadLibraries.Add(parts[1].Trim());
                }
            }
        }
        if (_runLaunchExecutables.Count == 0)
        {
            _runLaunchExecutables.Add("RetroRewind-Win64-Shipping.exe");
            _runForceLoadLibraries.Add("dwmapi.dll");
        }

        _runArguments = values.GetValueOrDefault("settings.runArguments") ?? "";
        _modManagerPath = values.GetValueOrDefault("settings.modManagerPath") ?? "";
        _modManagerType = values.GetValueOrDefault("settings.modManagerType") ?? "";
        _nexusApiKey = NexusSecretStore.Load() ?? "";
        _steamApiKey = SteamSecretStore.Load() ?? "";
        if (!BundledFontFiles.ContainsKey(_selectedFont))
            _selectedFont = SupportedFonts[0];

        ApplySelectedPalette();
        ApplyFontSelection();
    }

    private void ApplySelectedPalette()
    {
        switch (_selectedPalette)
        {
            case "80s Synthwave":
                ApplyPalette(
                    "#0B0820", "#140D2E", "#1A1238", "#21164A", "#2C1A59", "#4A2B70",
                    "#FF2FB3", "#00E5FF", "#B51686", "#FFF0FF", "#E7DFFF", "#B8A9D6", "#7A6A95");
                break;

            case "Arcade Neon":
                ApplyPalette(
                    "#081018", "#101C2A", "#15283A", "#1D3448", "#24465D", "#35677A",
                    "#FF4D6D", "#FF8A5B", "#C92F52", "#FFF1D6", "#E8F1F2", "#A7BBC2", "#536A72");
                break;

            case "Sunset Drive":
                ApplyPalette(
                    "#1A0F16", "#27141E", "#321A25", "#40212C", "#552C37", "#70404A",
                    "#FF8C42", "#FFD166", "#D95D2B", "#FFF2D5", "#F4DCC8", "#C6A99C", "#725A55");
                break;

            case "Forest Terminal":
                ApplyPalette(
                    "#08130E", "#0E1E16", "#13271D", "#193226", "#214333", "#315443",
                    "#57D68D", "#9AFFC3", "#2D9B61", "#E8FFE9", "#D3EBD9", "#9CB8A4", "#506B59");
                break;

            case "60s Mod":
                ApplyPalette(
                    "#F4EBD0", "#E7D9B8", "#D8C79D", "#C9B789", "#B6A270", "#786B4F",
                    "#D65A31", "#E88945", "#A63E22", "#3A3026", "#4B4034", "#756A5B", "#625544");
                break;

            case "70s Psychedelic":
                ApplyPalette(
                    "#24131F", "#38202E", "#4A2937", "#5A3140", "#70404D", "#8A5960",
                    "#F2B134", "#F26B38", "#B23A48", "#FFE8B6", "#F8DDA9", "#C7A98A", "#8D7060");
                break;

            case "90s Arcade":
                ApplyPalette(
                    "#11102B", "#1C1A43", "#26245A", "#302D6B", "#3E397D", "#57519A",
                    "#7CFF00", "#00F0FF", "#C800FF", "#F8F7FF", "#E5E4F2", "#A8A7C4", "#77769B");
                break;

            case "Retro Rewind":
                ApplyPalette(
                    "#0A0E17", "#0F1D26", "#111F28", "#14212A", "#1A262E", "#263030",
                    "#125F6F", "#146272", "#0E3846", "#FEE1B5", "#FEDDAA", "#FED18B", "#415151");
                break;

            case "":
            default:
                ApplyPalette(
                    "#F4EBD0", "#E7D9B8", "#D8C79D", "#C9B789", "#B6A270", "#786B4F",
                    "#D65A31", "#E88945", "#A63E22", "#3A3026", "#4B4034", "#756A5B", "#625544");
                _selectedPalette = "60s Mod";
                break;
        }
    }

    private void ApplyPalette(
        string background, string surface, string panel, string inner, string hover, string border,
        string accent, string accentBright, string accentPressed,
        string title, string text, string muted, string label)
    {
        Resources["WindowBackgroundBrush"] = Brush(background);
        Resources["ButtonBackgroundBrush"] = Brush(surface);
        Resources["CardBrush"] = Brush(panel);
        Resources["SecondaryCardBrush"] = Brush(inner);
        Resources["InputBackgroundBrush"] = Brush(inner);
        Resources["BorderBrush"] = Brush(border);
        Resources["SeparatorBrush"] = Brush(border);
        Resources["AccentBrush"] = Brush(accent);
        Resources["AccentHoverBrush"] = Brush(accentBright);
        Resources["AccentPressedBrush"] = Brush(accentPressed);
        Resources["AccentFocusBrush"] = Brush(accentBright);
        Resources["ForegroundBrush"] = Brush(title);
        Resources["SecondaryBrush"] = Brush(label);
        Resources["LabelBrush"] = Brush(label);
        Resources["AccentForegroundBrush"] = Brush(title);
        Resources["CheckForegroundBrush"] = Brush(title);
        Resources["CoreUiBackgroundBrush"] = Brush(background);
        Resources["CoreUiSurfaceBrush"] = Brush(surface);
        Resources["CoreUiPanelBrush"] = Brush(panel);
        Resources["CoreUiInnerBrush"] = Brush(inner);
        Resources["CoreUiHoverBrush"] = Brush(hover);
        Resources["CoreUiBorderBrush"] = Brush(border);
        Resources["CoreUiAccentBrush"] = Brush(accent);
        Resources["CoreUiAccentBrightBrush"] = Brush(accentBright);
        Resources["CoreUiTitleBrush"] = Brush(title);
        Resources["CoreUiTextBrush"] = Brush(text);
        Resources["CoreUiMutedBrush"] = Brush(muted);
        Resources["CoreUiLabelBrush"] = Brush(label);
        Resources["SidebarIconBrush"] = Brush(title);

        // Legacy aliases kept in sync so every button follows the active palette.
        Resources["TabBackgroundBrush"] = Brush(surface);
        Resources["TabForegroundBrush"] = Brush(title);
    }

    private void ApplySettingsButtonFeedback(Button button, bool accent)
    {
        void ApplyNormal()
        {
            button.Background = accent
                ? (Brush)Resources["AccentBrush"]
                : (Brush)Resources["ButtonBackgroundBrush"];
            button.Foreground = (Brush)Resources["ForegroundBrush"];
            button.BorderBrush = accent
                ? (Brush)Resources["AccentBrush"]
                : (Brush)Resources["BorderBrush"];
        }

        ApplyNormal();

        button.MouseEnter += (_, _) =>
        {
            button.Background = (Brush)Resources["AccentHoverBrush"];
            button.BorderBrush = (Brush)Resources["AccentHoverBrush"];
            button.Foreground = (Brush)Resources["AccentForegroundBrush"];
        };

        button.MouseLeave += (_, _) =>
        {
            if (!button.IsPressed)
                ApplyNormal();
        };

        button.PreviewMouseDown += (_, _) =>
        {
            button.Background = (Brush)Resources["AccentPressedBrush"];
            button.BorderBrush = (Brush)Resources["AccentPressedBrush"];
            button.Foreground = (Brush)Resources["AccentForegroundBrush"];
        };

        button.PreviewMouseUp += (_, _) =>
        {
            if (button.IsMouseOver)
            {
                button.Background = (Brush)Resources["AccentHoverBrush"];
                button.BorderBrush = (Brush)Resources["AccentHoverBrush"];
                button.Foreground = (Brush)Resources["AccentForegroundBrush"];
            }
            else
            {
                ApplyNormal();
            }
        };
    }

    private void SettingsComboBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ComboBox combo || !combo.IsEnabled) return;
        if (!combo.IsDropDownOpen)
        {
            combo.IsDropDownOpen = true;
            e.Handled = true;
        }
    }

    private void ApplySettingsPaletteFeedback(ComboBox combo)
    {
        combo.Background = (Brush)Resources["InputBackgroundBrush"];
        combo.Foreground = (Brush)Resources["ForegroundBrush"];
        combo.BorderBrush = (Brush)Resources["BorderBrush"];

        combo.DropDownOpened += (_, _) =>
        {
            foreach (var item in combo.Items)
            {
                if (combo.ItemContainerGenerator.ContainerFromItem(item) is ComboBoxItem container)
                {
                    container.Background = (Brush)Resources["InputBackgroundBrush"];
                    container.Foreground = (Brush)Resources["ForegroundBrush"];

                    container.MouseEnter += (_, _) =>
                    {
                        container.Background = (Brush)Resources["AccentHoverBrush"];
                        container.Foreground = (Brush)Resources["AccentForegroundBrush"];
                    };
                    container.MouseLeave += (_, _) =>
                    {
                        container.Background = container.IsSelected
                            ? (Brush)Resources["AccentBrush"]
                            : (Brush)Resources["InputBackgroundBrush"];
                        container.Foreground = container.IsSelected
                            ? (Brush)Resources["AccentForegroundBrush"]
                            : (Brush)Resources["ForegroundBrush"];
                    };
                }
            }
        };

        combo.MouseEnter += (_, _) =>
        {
            combo.BorderBrush = (Brush)Resources["AccentHoverBrush"];
        };
        combo.MouseLeave += (_, _) =>
        {
            if (!combo.IsDropDownOpen)
                combo.BorderBrush = (Brush)Resources["BorderBrush"];
        };
        combo.DropDownOpened += (_, _) =>
        {
            combo.BorderBrush = (Brush)Resources["AccentBrush"];
        };
        combo.DropDownClosed += (_, _) =>
        {
            combo.BorderBrush = (Brush)Resources["BorderBrush"];
        };
    }

    private void ApplySettingsDialogPalette(
        ContentControl dialog,
        TextBlock title,
        IEnumerable<TextBlock> labels,
        IEnumerable<Button> paletteButtons,
        TextBox saveText,
        TextBox modhubText,
        Button saveBrowse,
        Button modhubBrowse,
        Button cancel,
        Button apply)
    {
        // Slide panels are hosted inside MainWindow and therefore share its resource context.
        // Bind the dialog controls directly to the current palette brushes instead of
        // using DynamicResource, which would otherwise fall back to WPF defaults.
        var windowBrush = (Brush)Resources["WindowBackgroundBrush"];
        var foreground = (Brush)Resources["ForegroundBrush"];
        var input = (Brush)Resources["InputBackgroundBrush"];
        var border = (Brush)Resources["BorderBrush"];
        var button = (Brush)Resources["ButtonBackgroundBrush"];
        var accent = (Brush)Resources["AccentBrush"];
        var accentHover = (Brush)Resources["AccentHoverBrush"];
        var accentPressed = (Brush)Resources["AccentPressedBrush"];
        var accentForeground = (Brush)Resources["AccentForegroundBrush"];

        dialog.Background = windowBrush;
        dialog.Foreground = foreground;
        title.Foreground = foreground;

        if (dialog.Content is DependencyObject dialogRoot)
            ApplySettingsDialogThemeVisuals(dialogRoot);

        foreach (var label in labels)
            label.Foreground = foreground;

        foreach (var paletteButton in paletteButtons)
        {
            var isSelected = string.Equals(
                paletteButton.Tag as string, _selectedPalette, StringComparison.OrdinalIgnoreCase);

            // Selected palette gets a clear accent state and is disabled so it
            // cannot be clicked again. Keep opacity at 1 so the selected color
            // remains fully visible even though the button is disabled.
            paletteButton.IsEnabled = !isSelected;
            paletteButton.Opacity = 1.0;
            paletteButton.Background = isSelected ? accent : button;
            paletteButton.Foreground = isSelected ? accentForeground : foreground;
            paletteButton.BorderBrush = isSelected ? accent : border;
            paletteButton.ToolTip = isSelected
                ? null
                : $"Switch to {paletteButton.Tag}";
        }

        saveText.Background = input;
        saveText.Foreground = foreground;
        saveText.BorderBrush = border;

        modhubText.Background = input;
        modhubText.Foreground = foreground;
        modhubText.BorderBrush = border;

        saveBrowse.Background = button;
        saveBrowse.Foreground = foreground;
        saveBrowse.BorderBrush = border;

        modhubBrowse.Background = button;
        modhubBrowse.Foreground = foreground;
        modhubBrowse.BorderBrush = border;

        cancel.Background = button;
        cancel.Foreground = foreground;
        cancel.BorderBrush = border;

        apply.Background = accent;
        apply.Foreground = accentForeground;
        apply.BorderBrush = accent;

    }

    private void ApplySettingsDialogThemeVisuals(DependencyObject root)
    {
        if (root is Control control)
        {
            control.SetResourceReference(Control.ForegroundProperty, "ForegroundBrush");
            if (control is Button button)
            {
                button.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
                button.SetResourceReference(Control.BackgroundProperty, "ButtonBackgroundBrush");
                button.SetResourceReference(Control.ForegroundProperty, "ForegroundBrush");

                // Palette buttons and the primary Save Settings action use the accent state.
                var tag = button.Tag?.ToString();
                var content = button.Content?.ToString();
                if ((tag != null && SupportedFonts.Contains(tag, StringComparer.OrdinalIgnoreCase)) ||
                    (tag != null && string.Equals(tag, _selectedPalette, StringComparison.OrdinalIgnoreCase)) ||
                    string.Equals(content, L("Save Settings"), StringComparison.OrdinalIgnoreCase))
                {
                    button.SetResourceReference(Control.BackgroundProperty, "AccentBrush");
                    button.SetResourceReference(Control.BorderBrushProperty, "AccentBrush");
                    button.SetResourceReference(Control.ForegroundProperty, "AccentForegroundBrush");
                }
            }
            else if (control is TextBox textBox)
            {
                textBox.SetResourceReference(Control.BackgroundProperty, "InputBackgroundBrush");
                textBox.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
            }
            else if (control is PasswordBox passwordBox)
            {
                passwordBox.SetResourceReference(Control.BackgroundProperty, "InputBackgroundBrush");
                passwordBox.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
            }
            else if (control is TabControl tabControl)
            {
                tabControl.SetResourceReference(Control.BackgroundProperty, "WindowBackgroundBrush");
                tabControl.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
            }
        }
        else if (root is TextBlock textBlock)
        {
            textBlock.SetResourceReference(TextBlock.ForegroundProperty, "ForegroundBrush");
        }

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            ApplySettingsDialogThemeVisuals(VisualTreeHelper.GetChild(root, i));
    }

    private void LocalizeWindowText(DependencyObject parent)
    {
        if (parent is TextBlock tb && !string.IsNullOrWhiteSpace(tb.Text))
            tb.Text = Localization.Get(tb.Text);
        else if (parent is Button btn && btn.Content is string bs)
            btn.Content = Localization.Get(bs);
        else if (parent is CheckBox cb && cb.Content is string cs)
            cb.Content = Localization.Get(cs);
        else if (parent is Label label && !string.IsNullOrWhiteSpace(label.Content?.ToString()))
            label.Content = Localization.Get(label.Content.ToString()!);

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            LocalizeWindowText(VisualTreeHelper.GetChild(parent, i));
    }

    private static void MigrateLegacyAppFiles()
    {
        try
        {
            var appFolder = Path.Combine(AppContext.BaseDirectory, "RetroRewindModHub_Data");
            var legacyAppFolder = Path.Combine(AppContext.BaseDirectory, "RetroRewindStoreTransfer");
            var previousModhubFolder = Path.Combine(AppContext.BaseDirectory, "RetroRewindModhub");
            if (!Directory.Exists(appFolder))
            {
                if (Directory.Exists(previousModhubFolder))
                    Directory.Move(previousModhubFolder, appFolder);
                else if (Directory.Exists(legacyAppFolder))
                    Directory.Move(legacyAppFolder, appFolder);
            }
            Directory.CreateDirectory(appFolder);

            // Migrate any legacy root-level settings file into the support folder.
            var legacyConfig = Path.Combine(AppContext.BaseDirectory, "RetroRewindStoreTransfer.json");
            var newConfig = Path.Combine(appFolder, "RetroRewindModhub.json");
            if (File.Exists(legacyConfig) && !File.Exists(newConfig))
                File.Move(legacyConfig, newConfig);
        }
        catch { }
    }

    private static void MergeDirectoryContents(string source, string destination)
    {
        if (!Directory.Exists(source)) return;
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.TopDirectoryOnly))
        {
            var target = Path.Combine(destination, Path.GetFileName(file));
            if (!File.Exists(target))
            {
                try { File.Move(file, target); } catch { }
            }
        }

        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.TopDirectoryOnly))
        {
            if (IsUe4ssSpecialFolderName(Path.GetFileName(dir))) continue;
            var target = Path.Combine(destination, Path.GetFileName(dir));
            MergeDirectoryContents(dir, target);
            try
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
            }
            catch { }
        }
    }

    private static void MigrateLegacyUserData()
    {
        try
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var legacyRoot = Path.Combine(documents, "Retro Rewind - Blueprints");
            var newRoot = Path.Combine(documents, "Retro Rewind Modhub");
            var newBlueprints = Path.Combine(newRoot, "Blueprints");
            var newMods = Path.Combine(newRoot, "Mods");

            Directory.CreateDirectory(newRoot);
            Directory.CreateDirectory(newBlueprints);
            Directory.CreateDirectory(newMods);

            // The old folder stored blueprints directly at its root. Move those
            // files into the new dedicated Blueprints folder, while preserving
            // any existing folders/files that are already in the new layout.
            if (Directory.Exists(legacyRoot))
            {
                foreach (var file in Directory.EnumerateFiles(legacyRoot, "*", SearchOption.TopDirectoryOnly))
                {
                    var target = Path.Combine(newBlueprints, Path.GetFileName(file));
                    if (!File.Exists(target))
                    {
                        try { File.Move(file, target); } catch { }
                    }
                }

                foreach (var dir in Directory.EnumerateDirectories(legacyRoot, "*", SearchOption.TopDirectoryOnly))
                {
            if (IsUe4ssSpecialFolderName(Path.GetFileName(dir))) continue;
                    // Preserve any pre-existing Mods directory by merging it into
                    // the new persistent Mods location; other directories remain
                    // available under Blueprints.
                    var name = Path.GetFileName(dir);
                    var target = string.Equals(name, "Mods", StringComparison.OrdinalIgnoreCase)
                        ? newMods
                        : Path.Combine(newBlueprints, name);
                    MergeDirectoryContents(dir, target);
                }
            }

            // Previous builds kept Mods beside the executable. Move/merge it into
            // Documents so application updates cannot remove the user's mods.
            var legacyMods = Path.Combine(AppContext.BaseDirectory, "Mods");
            if (Directory.Exists(legacyMods))
            {
                MergeDirectoryContents(legacyMods, newMods);
                try { if (!Directory.EnumerateFileSystemEntries(legacyMods).Any()) Directory.Delete(legacyMods); } catch { }
            }

            // Also migrate the old root-level download store.
            var legacyDownloads = Path.Combine(AppContext.BaseDirectory, "_downloads");
            if (Directory.Exists(legacyDownloads))
                MergeDirectoryContents(legacyDownloads, Path.Combine(newMods, "_downloads"));

            // Copy the old application settings into the new user-data location
            // if the new settings file does not exist yet.
            var newConfig = Path.Combine(newRoot, "RetroRewindModhub.json");
            var oldAppConfig = Path.Combine(AppContext.BaseDirectory, "RetroRewindStoreTransfer", "RetroRewindStoreTransfer.json");
            var oldSupportConfig = Path.Combine(AppContext.BaseDirectory, "RetroRewindModhub", "RetroRewindModhub.json");
            if (!File.Exists(newConfig))
            {
                var source = File.Exists(oldAppConfig) ? oldAppConfig : oldSupportConfig;
                if (File.Exists(source))
                    File.Copy(source, newConfig, false);
            }

            // Normalize the migrated settings so the old Blueprint-folder key no
            // longer controls the location of the whole Modhub folder.
            if (File.Exists(newConfig))
            {
                try
                {
                    var values = JsonSerializer.Deserialize<Dictionary<string, string?>>(File.ReadAllText(newConfig))
                                 ?? new Dictionary<string, string?>();
                    var oldFolder = values.GetValueOrDefault("settings.blueprintFolder");
                    var oldModhubFolder = values.GetValueOrDefault("settings.modhubFolder");
                    if (!values.ContainsKey("settings.modsFolder"))
                    {
                        var sourceRoot = !string.IsNullOrWhiteSpace(oldModhubFolder)
                            ? oldModhubFolder
                            : oldFolder;
                        values["settings.modsFolder"] =
                            string.IsNullOrWhiteSpace(sourceRoot) ||
                            string.Equals(sourceRoot, legacyRoot, StringComparison.OrdinalIgnoreCase)
                                ? newMods
                                : Path.Combine(sourceRoot, "Mods");
                    }
                    values.Remove("settings.modhubFolder");
                    values.Remove("settings.blueprintFolder");
                    File.WriteAllText(newConfig, JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch { }
            }
        }
        catch { }
    }

    private string L(string text) => Localization.Get(text);

    private string L(string text, params object[] args) => Localization.Get(text, args);

    private static System.Windows.Media.FontFamily CreateFontFamily(string name)
    {
        if (!BundledFontFiles.TryGetValue(name, out var fileName))
            fileName = BundledFontFiles[SupportedFonts[0]];

        // WPF loads packaged fonts using a pack base URI plus a relative
        // font-family reference. The font file itself is a Resource in the
        // project, so this also works in a published/single-file build.
        var baseUri = new Uri("pack://application:,,,/", UriKind.Absolute);
        var familyReference = $"./Assets/Fonts/#${name}".Replace("#$", "#");

        // Gillius ADF No2's internal family name includes "Cond".
        if (string.Equals(name, "Gillius ADF No2", StringComparison.OrdinalIgnoreCase))
            familyReference = "./Assets/Fonts/#Gillius ADF No2 Cond";

        return new System.Windows.Media.FontFamily(baseUri, familyReference);
    }

    private void ApplyFontToVisualTree(DependencyObject root, System.Windows.Media.FontFamily font)
    {
        // Apply explicitly to controls as well as the Window. This prevents
        // individual styles/templates from keeping an older local font.
        if (root is Control control)
            control.FontFamily = font;
        else if (root is TextBlock textBlock)
            textBlock.FontFamily = font;

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            ApplyFontToVisualTree(VisualTreeHelper.GetChild(root, i), font);
    }

    private void ApplyFontSelection()
    {
        var requested = _selectedFont;
        var effective = BundledFontFiles.ContainsKey(requested) ? requested : FontFallback;
        if (!string.Equals(_selectedFont, effective, StringComparison.OrdinalIgnoreCase))
            _selectedFont = effective;
        var font = CreateFontFamily(effective);

        FontFamily = font;
        ApplyFontToVisualTree(this, font);
    }

    private void ApplyLanguage()
    {
        // Main All Objects group controls.
        ObjectsDecorationsButton.Content = L("Decorations");
        ObjectsEquipmentButton.Content = L("Equipment");
        ObjectsShelvesButton.Content = L("Shelves");
        ObjectsExcludedButton.Content = L("Excluded");
        LaunchGameButtonText.Text = L("Launch Retro Rewind");
        HealthCheckTabText.Text = L("Health Check");
        ModManagerGroupText.Text = L("Mod Manager");
        SaveManagerGroupText.Text = L("Save Manager");
        ModManagerTabText.Text = L("Mods");
        MergeModsTabText.Text = L("Merge Mods");
        ConfigureModsTabText.Text = L("Configure Mods");
        VideosTabText.Text = L("Videos");
        VideoEditorTabText.Text = L("Video Editor");
        ConflictCheckTabText.Text = L("Conflict Checker");
        ConflictCheckTab.ToolTip = L("Conflict Checker");
        TransferTabText.Text = L("Store Transfer");
        ExportTabText.Text = L("Store Blueprint");
        InfoTabText.Text = L("Save Information");
        StoreManagementTabText.Text = L("Store Demolition");
        ConfigureModsTabText.Visibility = _sidebarExpanded ? Visibility.Visible : Visibility.Collapsed;
        HomeButtonText.Text = L("Home");
        SettingsButtonText.Text = L("Settings");
        PakModsHeader.Text = L("PAK MODS");

        // Keep the object-list data names unchanged; only UI labels are localized.
        // Count text is refreshed by SetObjectGroup; localization files
        // supply the translated labels used by the rest of the window.
    }

    private static bool HasTrayStartupArgument()
    {
        return Environment.GetCommandLineArgs()
            .Skip(1)
            .Any(a => string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase));
    }

    private static string? GetCurrentExecutablePath()
    {
        try
        {
            return Process.GetCurrentProcess().MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static void UpdateWindowsAutoStartRegistration(bool enabled)
    {
        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true)
                ?? Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");

            if (runKey == null) return;

            const string valueName = "RetroRewindModhub";
            if (!enabled)
            {
                runKey.DeleteValue(valueName, throwOnMissingValue: false);
                return;
            }

            var executable = GetCurrentExecutablePath();
            if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable)) return;

            runKey.SetValue(valueName, $"\"{executable}\" --tray");
        }
        catch (Exception ex)
        {
            CrashLogger.Write("UpdateWindowsAutoStartRegistration", ex);
        }
    }

    private void MainWindow_TrayStartupLoaded(object? sender, RoutedEventArgs e)
    {
        InitializeTrayIcon();
        if (HasTrayStartupArgument())
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                Hide();
                ShowInTaskbar = false;
            }), DispatcherPriority.ApplicationIdle);
        }
    }

    private void InitializeTrayIcon()
    {
        if (_trayIcon != null) return;

        try
        {
            _trayMenu = new Forms.ContextMenuStrip();
            _trayMenu.Items.Add(CreateTrayItem(L("Home"), "home"));
            _trayMenu.Items.Add(new Forms.ToolStripSeparator());
            _trayMenu.Items.Add(CreateTrayItem(L("Mods"), "mods"));
            _trayMenu.Items.Add(CreateTrayItem(L("Configure Mods"), "configuremods"));
            _trayMenu.Items.Add(CreateTrayItem(L("Videos"), "videos"));
            _trayMenu.Items.Add(new Forms.ToolStripSeparator());
            _trayMenu.Items.Add(CreateTrayItem(L("Store Transfer"), "transfer"));
            _trayMenu.Items.Add(CreateTrayItem(L("Store Blueprint"), "export"));
            _trayMenu.Items.Add(CreateTrayItem(L("Store Import"), "import"));
            _trayMenu.Items.Add(CreateTrayItem(L("Save Info"), "info"));
            _trayMenu.Items.Add(new Forms.ToolStripSeparator());
            var settings = new Forms.ToolStripMenuItem(L("Settings"));
            settings.Click += (_, _) => OpenSettingsFromTray();
            _trayMenu.Items.Add(settings);
            _trayMenu.Items.Add(new Forms.ToolStripSeparator());
            var launchGame = new Forms.ToolStripMenuItem(L("Launch Game"));
            launchGame.Click += (_, _) => LaunchGameFromTray();
            _trayMenu.Items.Add(launchGame);
            var openNexus = new Forms.ToolStripMenuItem(L("Open Nexus Mods"));
            openNexus.Click += (_, _) => OpenNexusFromTray();
            _trayMenu.Items.Add(openNexus);
            _trayMenu.Items.Add(new Forms.ToolStripSeparator());
            var exit = new Forms.ToolStripMenuItem(L("Exit ModHub"));
            exit.Click += (_, _) => ExitFromTray();
            _trayMenu.Items.Add(exit);

            _trayIcon = new Forms.NotifyIcon
            {
                Text = L("Retro Rewind ModHub"),
                ContextMenuStrip = _trayMenu,
                Visible = true
            };

            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/RetroRewindModHub.ico"));
            if (resource?.Stream != null)
                _trayIcon.Icon = new System.Drawing.Icon(resource.Stream);
            else
                _trayIcon.Icon = System.Drawing.SystemIcons.Application;

            _trayIcon.MouseClick += TrayIcon_MouseClick;
            _trayIcon.DoubleClick += (_, _) => ShowFromTray();
        }
        catch (Exception ex)
        {
            CrashLogger.Write("InitializeTrayIcon", ex);
        }
    }

    private Forms.ToolStripMenuItem CreateTrayItem(string text, string mode)
    {
        var item = new Forms.ToolStripMenuItem(text);
        item.Click += (_, _) => OpenTrayPage(mode);
        return item;
    }

    private void TrayIcon_MouseClick(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Left)
            ShowFromTray();
    }

    private void ShowFromTray()
    {
        ShowInTaskbar = true;
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void OpenTrayPage(string mode)
    {
        ShowFromTray();
        RememberCurrentPaths();
        _mode = mode;
        UpdateMode();
        RestoreRememberedPaths();
    }

    private void OpenSettingsFromTray()
    {
        ShowFromTray();
        Settings_Click(this, new RoutedEventArgs());
    }

    private void LaunchGameFromTray()
    {
        ShowFromTray();
        LaunchGame_Click(this, new RoutedEventArgs());
    }

    private static void OpenNexusFromTray()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://www.nexusmods.com/retrorewindvideostoresimulator",
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void ExitFromTray()
    {
        if (_shutdownStarted) return;
        if (!IsVisible)
        {
            Show();
            ShowInTaskbar = true;
        }
        BeginShutdown();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowWindowClose)
        {
            if (_closeToTaskbar)
            {
                e.Cancel = true;
                HideToTaskbar();
                return;
            }

            e.Cancel = true;
            BeginShutdown();
            return;
        }

        try { _trayIcon?.Dispose(); } catch { }
        _trayIcon = null;
        try { _trayMenu?.Dispose(); } catch { }
        _trayMenu = null;
        base.OnClosing(e);
    }

    private void BeginShutdown()
    {
        if (_shutdownStarted) return;
        _shutdownStarted = true;
        ShutdownOverlay.Visibility = Visibility.Visible;
        RootLayout.IsEnabled = false;
        ShutdownOverlayProgress.Value = 0;
        ShutdownOverlayStatus.Text = "Preparing to close…";
        ShutdownOverlayDetail.Text = "Stopping background services…";
        try { Activate(); } catch { }
        _ = PerformShutdownAsync();
    }

    private async Task PerformShutdownAsync()
    {
        try
        {
            await RunShutdownStepAsync("Stopping background services…", "Game and performance monitoring", 15, () =>
            {
                try { _gameActivityTimer?.Stop(); } catch { }
                _gameActivityTimer = null;
                try { _resourceUsageTimer?.Stop(); } catch { }
                _resourceUsageTimer = null;
                try { _downloadUiTimer?.Stop(); } catch { }
                _downloadUiTimer = null;
                try { _infoSaveWatcher?.Stop(); } catch { }
                _infoSaveWatcher = null;
                try { _sidebarAnimationTimer?.Stop(); } catch { }
                _sidebarAnimationTimer = null;
                try { _sidebarGroupAnimationTimer?.Stop(); } catch { }
                _sidebarGroupAnimationTimer = null;
            });

            await RunShutdownStepAsync("Cancelling background work…", "Finishing pending scans and metadata tasks", 35, () =>
            {
                try { _modRefreshCts?.Cancel(); } catch { }
                try { _videoLibraryRefreshCts?.Cancel(); } catch { }
                try { _downloadsRefreshCts?.Cancel(); } catch { }
                try { _nexusBackgroundCts?.Cancel(); } catch { }
                try { _homeNewsCts?.Cancel(); } catch { }
                try { _conflictScanCts?.Cancel(); } catch { }
                try { _videoEditorPreviewCts?.Cancel(); } catch { }
            });

            await RunShutdownStepAsync("Releasing media…", "Closing video playback and preview resources", 55, () =>
            {
                try { _videoEditorPreviewTimer.Stop(); } catch { }
                try { StopAndReleaseVideoEditorPreview(disposePlayer: true); } catch { }
                try { _videoEditorEffectsOverlayWindow?.Close(); } catch { }
                _videoEditorEffectsOverlayWindow = null;
                try { _resourceUsageProcess?.Dispose(); } catch { }
                _resourceUsageProcess = null;
            });

            await RunShutdownStepAsync("Closing monitors and UI resources…", "Releasing remaining application resources", 75, () =>
            {
                try { _modRefreshCts?.Dispose(); } catch { }
                _modRefreshCts = null;
                try { _videoLibraryRefreshCts?.Dispose(); } catch { }
                _videoLibraryRefreshCts = null;
                try { _downloadsRefreshCts?.Dispose(); } catch { }
                _downloadsRefreshCts = null;
                try { _nexusBackgroundCts?.Dispose(); } catch { }
                _nexusBackgroundCts = null;
                try { _homeNewsCts?.Dispose(); } catch { }
                _homeNewsCts = null;
                try { _conflictScanCts?.Dispose(); } catch { }
                _conflictScanCts = null;
                try { _videoEditorPreviewCts?.Dispose(); } catch { }
                _videoEditorPreviewCts = null;
            });

            await RunShutdownStepAsync("Saving and exiting…", "ModHub is closing safely", 92, () =>
            {
                try { RememberCurrentPaths(); } catch { }
                try { _trayIcon?.Dispose(); } catch { }
                _trayIcon = null;
                try { _trayMenu?.Dispose(); } catch { }
                _trayMenu = null;
            });

            ShutdownOverlayStatus.Text = "Goodbye!";
            ShutdownOverlayDetail.Text = "Retro Rewind ModHub has finished shutting down.";
            ShutdownOverlayProgress.Value = 100;
            await Task.Delay(80);
        }
        catch (Exception ex)
        {
            CrashLogger.Write("Shutdown", ex);
        }
        finally
        {
            _allowWindowClose = true;
            try { Close(); } catch { Application.Current.Shutdown(); }
        }
    }

    private async Task RunShutdownStepAsync(string status, string detail, double progress, Action action)
    {
        ShutdownOverlayStatus.Text = status;
        ShutdownOverlayDetail.Text = detail;
        ShutdownOverlayProgress.Value = progress;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        action();
        await Task.Yield();
    }
}
